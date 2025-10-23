
using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;

namespace Syebun.WaitingLine
{
    enum AdminMode
    {
        None = 0,
        InstanceOwner,
        InList
    }

    enum WaitMode
    {
        Leave = -1,
        Wait = 0,
        Join = 1
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class WaitingLineController : UdonSharpBehaviour
    {
#region 変数
        // [ 設定用 ]
        [Header("権限モード")]
        [SerializeField]
        private AdminMode adminMode;

        [Header("権限付与ユーザ名")]
        [SerializeField]
        private string[] slAdminList;

        [Header("順番が来た際の表示文字列")]
        [SerializeField]
        private string sNoticeMessage = "順番が来ました\n指定の場所へお越しください";

        [Header("待機列が無い場合の表示文字列")]
        [SerializeField]
        private string sNonLineMessage = "待機中";

        [Header("待機列の最大数")]
        [SerializeField]
        private int iMaxLine = 80;

        [Header("他Udon連携用")]
        [SerializeField]
        [Tooltip("window一覧")]
        private WaitingLineWindow[] m_WaitingLineWindows;

        [SerializeField]
        [Tooltip("Toast")]
        private NoticeToast m_NoticeToast = null;

        // [ 同期 ]
        [UdonSynced]
        [HideInInspector]
        public string[] slWaitingPlayerNames = null; // 待機列参加中のユーザ名リスト

        [UdonSynced, FieldChangeCallback("sCallPlayerName")]
        private string _sCallPlayerName = ""; // 呼び出しユーザ名
        [HideInInspector]
        public string sCallPlayerName
        {
            set
            {
                _sCallPlayerName = value;
                SyncCallPlayerName();
            }
            get => _sCallPlayerName;
        }

        // [ ローカル ]
        private VRCPlayerApi lPlayer  = null; // ローカルユーザ情報
        private int iWaitingNo = -1; // 待機番号
#endregion

#region ライルサイクル
        void Start()
        {
            // Debug.Log($"WaitingLineController::Start()");
            lPlayer = Networking.LocalPlayer;

            if (Networking.IsOwner(gameObject))
            {
                Init();
            }

            SyncCallPlayerName();
            SyncWaitingPlayerNames();
            InitChangeJoinLeaveButton();
        }

        public override void OnDeserialization()
        {
            // Debug.Log($"WaitingLineController::OnDeserialization()");
            SyncCallPlayerName();
            SyncWaitingPlayerNames();
            InitChangeJoinLeaveButton();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi newOwner)
        {
            if (Networking.IsOwner(gameObject))
            {
                Init();
                SyncCallPlayerName();
                SyncWaitingPlayerNames();
            }
        }
#endregion

#region イベント
        /// <summary>
        /// sCallPlayerName同期時の処理
        /// </summary>
        private void SyncCallPlayerName()
        {
            // UI表示変更
            SetCallPlayer(sCallPlayerName);

            // 呼び出し対象が自分の場合はボタンモードをJoinにしておく
            string sLPName = (null == lPlayer) ? "" : lPlayer.displayName;
            if(sLPName == sCallPlayerName)
            {
                ChangeJoinLeaveButton((int)WaitMode.Join);
                if(null != m_NoticeToast)
                {
                    m_NoticeToast.ShowToast(sNoticeMessage);
                }
            }
        }

        /// <summary>
        /// 待機列参加処理をOwnerに依頼
        /// </summary>
        public void LineJoinTemp()
        { 
            // Debug.Log($"WaitingLineController::LineJoinTemp()");
            // ローカルプレイヤ情報が無い場合は処理終了
            if (null == lPlayer) return;
            string sLPName = lPlayer.displayName;

            ChangeJoinLeaveButton((int)WaitMode.Wait);
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(LineJoin), sLPName);
        }

        /// <summary>
        /// 待機列退出処理をOwnerに依頼
        /// </summary>
        public void LineLeaveTemp()
        {
            // ローカルプレイヤ情報が無い場合は処理終了
            if (null == lPlayer) return;
            string sLPName = lPlayer.displayName;

            ChangeJoinLeaveButton((int)WaitMode.Wait);
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(LineLeave), sLPName);
        }

        /// <summary>
        /// 待機列を進める権限がある場合は処理をOwnerに依頼
        /// </summary>
        public void NextPlayerTemp()
        {
            // Debug.Log($"WaitingLineController::NextPlayerTemp()");
            // ローカルプレイヤ情報が無い場合は処理終了
            if (null == lPlayer) return;
            string sLPName = lPlayer.displayName;

            // 権限が無い場合は処理終了
            if(false == InAdmin(sLPName)) return;
            
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NextPlayer));
        }
#endregion

#region SendCustomNetworkEvent用
        /// <summary>
        /// 指定ユーザを待機列に参加
        /// </summary>
        /// <param name="sName">ユーザ名</param>
        [NetworkCallable]
        public void LineJoin(string sName)
        {
            // 基本的にOwnerのみ実行するためチェック
            if(!Networking.IsOwner(gameObject)) return;

            // 待機列参加済みかチェック
            int index = Array.IndexOf(slWaitingPlayerNames, sName);
            if(0 <= index)
            {
                // すでに待機列にいるので列参加者に参加済みを通知
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendJoinLeave), sName, (int)WaitMode.Leave);
                return;
            }

            // 待機列の末尾に追加
            int iLastIndex = LastUsedIndex(slWaitingPlayerNames);
            slWaitingPlayerNames[iLastIndex+1] = sName;
            // Debug.Log($"WaitingLineController::LineLeave(), displayName = {lPlayer.displayName}, slWaitingPlayerNames = {string.Join(", ", slWaitingPlayerNames)}");

            // 変数同期と同期時処理実行
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncWaitingPlayerNames));

            // 列参加者に処理完了を通知
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendJoinLeave), sName, (int)WaitMode.Leave);
        }

        /// <summary>
        /// 指定ユーザを待機列から退出
        /// </summary>
        /// <param name="sName">ユーザ名</param>
        [NetworkCallable]
        public void LineLeave(string sName)
        {
            // 基本的にOwnerのみ実行するためチェック
            if(!Networking.IsOwner(gameObject)) return;

            // 待機列内の位置を取得
            int index = Array.IndexOf(slWaitingPlayerNames, sName);
            if(0 > index)
            {
                // 待機列にいないので列参加者に未参加を通知
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendJoinLeave), sName, (int)WaitMode.Join);
                return;
            }

            // 指定配列位置を除いた配列を作成
            string[] strings = new string[slWaitingPlayerNames.Length]; 
            if(0 < index) Array.Copy(slWaitingPlayerNames, strings, index);
            Array.Copy(slWaitingPlayerNames, index+1, strings, index, slWaitingPlayerNames.Length-index-1);

            // 同期用変数に格納してSync
            Array.Copy(strings, slWaitingPlayerNames, strings.Length);
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncWaitingPlayerNames));

            // 列参加者に処理完了を通知
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SendJoinLeave), sName, (int)WaitMode.Join);
        }

        /// <summary>
        /// 列参加者に処理完了を通知
        /// </summary>
        /// <param name="sName">列参加者名</param>
        /// <param name="waitMode">列参加者の状態</param>
        [NetworkCallable]
        public void SendJoinLeave(string sName, int waitMode)
        {
            string sLPName = (null == lPlayer) ? "" : lPlayer.displayName;

            // 列参加者の場合は状態を更新
            if(sLPName == sName)
            {
                ChangeJoinLeaveButton(waitMode);
            }
        }

        /// <summary>
        /// 待機列を進める
        /// </summary>
        [NetworkCallable]
        public void NextPlayer()
        {
            // Debug.Log($"WaitingLineController::NextPlayer()");
            // 基本的にOwnerのみ実行するためチェック
            if(!Networking.IsOwner(gameObject)) return;

            // インスタンス内のプレイヤリスト取得
            string[] sInstansPlayers = GetInstansPlayerNames();

            // 先頭から順にインスタンスに存在するユーザ名を取得
            int iNextPlayerIndex = 0;
            foreach(string sNextPName in slWaitingPlayerNames)
            {
                // ユーザ名が空もしくはnullの場合は待機列無しとする
                if(true == string.IsNullOrEmpty(sNextPName))
                {
                    sCallPlayerName = sNonLineMessage;
                    break;
                }

                // インスタンス内にいるかチェック
                if(0 <= Array.IndexOf(sInstansPlayers, sNextPName))
                {
                    // 先頭のユーザ名を同期用変数に格納
                    sCallPlayerName = sNextPName;
                    break;
                }

                iNextPlayerIndex++;
            }
            
            // 呼び出し対象より前を削除した配列を作成
            string[] strings = new string[slWaitingPlayerNames.Length]; 
            Array.Copy(slWaitingPlayerNames, iNextPlayerIndex + 1, strings, iNextPlayerIndex, slWaitingPlayerNames.Length - iNextPlayerIndex - 1);

            // 同期用変数に格納してSync
            Array.Copy(strings, slWaitingPlayerNames, strings.Length);
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncWaitingPlayerNames));
        }

        /// <summary>
        /// slWaitingPlayerNames同期時の処理
        /// </summary>
        [NetworkCallable]
        public void SyncWaitingPlayerNames()
        {
            string sLPName = (null == lPlayer) ? "" : lPlayer.displayName;

            // 現在の待機番号を取得して格納
            int index = Array.IndexOf(slWaitingPlayerNames, sLPName);
            iWaitingNo = index;

            // Windowを更新
            SetWaitingNo(iWaitingNo);
            SetWaitingList(slWaitingPlayerNames);
        }
#endregion

#region UI操作
        /// <summary>
        /// 列参加ボタンの状態を更新
        /// </summary>
        /// <param name="iMode">列参加状態</param>
        private void ChangeJoinLeaveButton(int iMode)
        {
            // Debug.Log($"WaitingLineController::ChangeJoinLeaveButton(), iMode = {iMode}");
            // 各Windowを更新
            foreach(WaitingLineWindow WLW in m_WaitingLineWindows)
            {
                if(null == WLW) continue;
                WLW.SetJoinLeaveBtn(iMode);
            }
        }

        /// <summary>
        /// 呼び出し者の表示を更新
        /// </summary>
        /// <param name="sPlayerName">呼び出しユーザ名</param>
        private void SetCallPlayer(string sPlayerName)
        {
            // 各Windowを更新
            foreach(WaitingLineWindow WLW in m_WaitingLineWindows)
            {
                if(null == WLW) continue;
                WLW.SetCallPlayer(sPlayerName);
            }
        }

        /// <summary>
        /// 待機列状態表示を更新
        /// </summary>
        /// <param name="slWaitingList">待機列参加者リスト</param>
        private void SetWaitingList(string[] slWaitingList)
        {
            // 各Windowを更新
            foreach(WaitingLineWindow WLW in m_WaitingLineWindows)
            {
                if(null == WLW) continue;
                WLW.SetWaitingList(slWaitingList);
            }
        }

        /// <summary>
        /// 待機列番号を更新
        /// </summary>
        /// <param name="iNo">待機列番号</param>
        private void SetWaitingNo(int iNo)
        {
            // 各Windowを更新
            foreach(WaitingLineWindow WLW in m_WaitingLineWindows)
            {
                if(null == WLW) continue;
                WLW.SetWaitingNo(iNo);
            }
        }
#endregion

#region ローカルメソッド
        /// <summary>
        /// 列を進める権限があるか
        /// </summary>
        /// <param name="message">トーストのメッセージ</param>
        /// <return>
        /// true: 権限あり
        /// false: 権限なし
        /// </return>
        private bool InAdmin(string sName)
        {
            bool bAdmin = false;

            switch(adminMode)
            {
                case AdminMode.None: 
                    // 権限制限が無いため常時True
                    bAdmin =  true;
                    break;
                case AdminMode.InstanceOwner:
                    // インスタンスオーナの場合はTrue
                    // ローカルプレイヤ情報が無い場合は処理終了
                    if (lPlayer == null) break;
                    bAdmin = (true == lPlayer.isInstanceOwner);
                    break;
                case AdminMode.InList:
                    // 権限リストにある場合はTrue
                    var index = Array.IndexOf(slAdminList, sName);
                    bAdmin = (0 <= index);
                    break;
            }
            
            return bAdmin;
        }

        /// <summary>
        /// 配列の使用済み末尾を返却
        /// </summary>
        /// <param name="message">トーストのメッセージ</param>
        /// <retrun>配列使用末尾の配列番号</retrun>
        private int LastUsedIndex(string[] sArray)
        {
            if (sArray == null) return -1;
            for (int i = sArray.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(sArray[i])) return i;
            }
                
            return -1;
        }

        /// <summary>
        /// ボタンの初期状態設定
        /// </summary>
        private void InitChangeJoinLeaveButton()
        {
            string sLPName = (null == lPlayer) ? "" : lPlayer.displayName;
            int index = Array.IndexOf(slWaitingPlayerNames, sLPName);

            if(0 > index)
            {
                // 待機列未参加
                ChangeJoinLeaveButton((int)WaitMode.Join);
            }
            else
            {
                // 待機列参加済み
                ChangeJoinLeaveButton((int)WaitMode.Leave);
            }
        }

        /// <summary>
        /// インスタンス内のプレイヤ情報取得
        /// </summary>
        /// <retrun>インスタンス内のプレイヤ情報配列</retrun>
        private VRCPlayerApi[] GetInstansPlayers()
        {
            int playerCount = VRCPlayerApi.GetPlayerCount();
            if (0 >= playerCount) return null;

            VRCPlayerApi[] instansPlayers = new VRCPlayerApi[playerCount];
            VRCPlayerApi.GetPlayers(instansPlayers);

            return instansPlayers;
        }

        /// <summary>
        /// インスタンス内のユーザ名取得
        /// </summary>
        /// <retrun>インスタンス内のユーザ名配列</retrun>
        private string[] GetInstansPlayerNames()
        {
            VRCPlayerApi[] instansPlayers = GetInstansPlayers();
            if (null == instansPlayers) return null;

            int playerCount = VRCPlayerApi.GetPlayerCount();
            string[] sInstansPNames = new string[playerCount];
            for(int i = 0; i < playerCount; i++)
            {
                sInstansPNames[i] = instansPlayers[i].displayName;
            }

            return sInstansPNames;
        }
        
        /// <summary>
        /// 初期化処理
        /// </summary>
        private void Init()
        {
            bool bSync = false;

            if(null == slWaitingPlayerNames || slWaitingPlayerNames.Length != iMaxLine)
            {
                slWaitingPlayerNames = new string[iMaxLine];
                bSync = true;
            }

            if(true == string.IsNullOrEmpty(sCallPlayerName))
            {
                sCallPlayerName = sNonLineMessage;
                bSync = true;
            }

            // 初期化したものがある場合はSync
            if(true == bSync)
            {
                RequestSerialization();
                SendCustomNetworkEvent(NetworkEventTarget.All, nameof(SyncWaitingPlayerNames));
            }
        }
#endregion
    }
}

