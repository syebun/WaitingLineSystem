
using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using TMPro;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;

namespace Syebun.WaitingLine
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class WaitingLineWindow : UdonSharpBehaviour
    {
#region 変数
        // [ 設定用 ]
        [Header("他Udon連携用")]
        [SerializeField]
        [Tooltip("Controller")]
        private WaitingLineController m_WLController;

        [Header("UI系")]
        [SerializeField]
        private Button m_BtnJoin;
        [SerializeField]
        private Button m_BtnLeave;
        [SerializeField]
        private Button m_BtnWait;
        
        [SerializeField]
        private TextMeshProUGUI m_TextCallPlayer;
        [SerializeField]
        private TextMeshProUGUI m_TextWaitNo;
        [SerializeField]
        private TextMeshProUGUI m_TextWaitList;

        // [ ローカル ]
#endregion

#region ライルサイクル
        void Start()
        {
            
        }
#endregion

#region イベント
        /// <summary>
        /// 参加ボタン押下時
        /// </summary>
        public void OnClickJoinBtn()
        {
            // Controllerに処理を委託
            if(null == m_WLController) return;
            m_WLController.LineJoinTemp();
        }

        /// <summary>
        /// 退出ボタン押下時
        /// </summary>
        public void OnClickLeaveBtn()
        {
            // Controllerに処理を委託
            if(null == m_WLController) return;
            m_WLController.LineLeaveTemp();
        }

        /// <summary>
        /// 待機列を進めるボタン押下時
        /// </summary>
        public void OnClickCallBtn()
        {
            // Controllerに処理を委託
            if(null == m_WLController) return;
            m_WLController.NextPlayerTemp();
        }
#endregion

#region UI操作
        /// <summary>
        /// 待機列参加退出ボタンの状態設定
        /// </summary>
        /// <param name="iMode">待機列参加状態</param>
        public void SetJoinLeaveBtn(int iMode)
        {
            m_BtnJoin.gameObject.SetActive(0 < iMode); // 0 < iMode: Join表示
            m_BtnLeave.gameObject.SetActive(0 > iMode); // 0 > iMode: Leave表示
            m_BtnWait.gameObject.SetActive(0 == iMode); // 0 == iMode: Wait表示（処理中）
        }

        /// <summary>
        /// 呼び出し者表示変更
        /// </summary>
        /// <param name="sPlayerName">ユーザ名</param>
        public void SetCallPlayer(string sPlayerName)
        {
            m_TextCallPlayer.text = sPlayerName;
        }

        /// <summary>
        /// 待機番号表示変更
        /// </summary>
        /// <param name="iNo">待機列番号</param>
        public void SetWaitingNo(int iNo)
        {
            if(0 > iNo)
            {
                m_TextWaitNo.text = $"待機列に未参加です";
            }
            else
            {
                m_TextWaitNo.text = $"呼び出しまで：{iNo+1}人";
            }
        }

        /// <summary>
        /// 待機列状態表示更新
        /// </summary>
        /// <param name="slWaitingList">待機列参加ユーザ名配列</param>
        public void SetWaitingList(string[] slWaitingList)
        {
            string sText = CreateWaitingListText(slWaitingList);
            m_TextWaitList.text = sText;
        }
#endregion

#region ローカルメソッド
        /// <summary>
        /// 待機列参加ユーザ名配列から表示用文字列を生成
        /// </summary>
        /// <param name="slWaitingList">待機列参加ユーザ名配列</param>
        /// <retrun>待機列状態表示用文字列</retrun>
        private string CreateWaitingListText(string[] slWaitingList)
        {
            string sText = "";
            for(int i = 0; i < slWaitingList.Length; i++)
            {
                if(true == string.IsNullOrEmpty(slWaitingList[i])) break;
                if(0 < i) sText += $"\n";
                sText += $"{i+1}. {slWaitingList[i]}";
            }

            return sText;
        }
#endregion
    }
}

