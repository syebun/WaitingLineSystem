
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
    enum NoticeState
    {
        STATE_IDLE = 0,
        STATE_FADING_IN,
        STATE_VISIBLE,
        STATE_FADING_OUT
    }

    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoticeToast : UdonSharpBehaviour
    {
#region 変数
        // [ 設定用 ]
        [Header("参加時のメッセージ機能を有効にするか")]
        public bool enableOnStart = false;
        [Header("参加時のメッセージ内容")]
        public string startMessage = "待機列は管理システムで管理しています";

        [Header("Animation Settings")]
        [Tooltip("1フレームの秒数")]
        public float display1F = 0.05f;

        [Tooltip("フェードインの総時間（秒）")]
        public float fadeInDuration = 0.5f;

        [Tooltip("フェードアウトの総時間（秒）")]
        public float fadeOutDuration = 0.5f;

        [Tooltip("トースト表示後の保持時間（秒）")]
        public float displayDuration = 10f;

        [Header("UI系")]
        [Tooltip("トースト内のメッセージテキスト")]
        public TextMeshProUGUI messageText;
        [Tooltip("トーストの透明度を制御するCanvasGroup")]
        public CanvasGroup canvasGroup;
        [Tooltip("トーストの進捗バーとして使用するImage")]
        public Image progressBar;
        [Tooltip("トーストを表示する際に鳴らすAudioSource")]
        public AudioSource audioSource;

        // [ ローカル ]
        private NoticeState currentState = NoticeState.STATE_IDLE;

        // アニメーションステップの透明度増減量
        private float fadeInStepAmount; // 開始側の1ステップの透明度増減量
        private float fadeOutStepAmount; // 終了側の1ステップの透明度増減量

        // 進捗バーの1ステップごとの幅減少量
        private float progressStepAmount;

        // 進捗バーのステップ数
        private int progressSteps; 

        // 最大幅の保存
        private float maxProgressWidth;

        // 進捗バーの現在のステップ数
        private int currentProgressStep = 0;
        // フェードアウト後に表示するトーストの内容を保持
        private string pendingToastMessage = "";
#endregion

#region ライルサイクル
        void Start()
        {
            // 初期状態を設定
            canvasGroup.gameObject.SetActive(false);
            canvasGroup.alpha = 0f;
            progressBar.rectTransform.sizeDelta = new Vector2(
                progressBar.rectTransform.sizeDelta.x,
                progressBar.rectTransform.sizeDelta.y
            );

            // ステップごとの遅延時間と透明度増減量を計算
            fadeInStepAmount = 1f / (fadeInDuration / display1F);

            fadeOutStepAmount = 1f / (fadeOutDuration / display1F);

            // 進捗バーの初期設定
            maxProgressWidth = progressBar.rectTransform.sizeDelta.x;
            progressSteps = (int)Math.Floor(displayDuration / display1F);
            progressStepAmount = maxProgressWidth / progressSteps;
            progressBar.rectTransform.sizeDelta = new Vector2(
                maxProgressWidth,
                progressBar.rectTransform.sizeDelta.y
            );

            // 参加時のメッセージ機能が有効の場合は表示
            if (true == enableOnStart) ShowToast(startMessage);
        }
#endregion

#region 外部から呼出し用
        /// <summary>
        /// トーストを表示するメソッド
        /// </summary>
        /// <param name="message">トーストのメッセージ</param>
        public void ShowToast(string message)
        {
            if (currentState == NoticeState.STATE_FADING_IN || currentState == NoticeState.STATE_VISIBLE)
            {
                // 現在表示中の場合、トーストを即座にフェードアウトさせてから新しいトーストを表示
                pendingToastMessage = message;

                // 通知音再生中の場合は一度停止してから再生する
                if (true == audioSource.isPlaying) 
                {
                    audioSource.Stop();
                }
                audioSource.Play();

                // 状態をフェードアウトに変更
                currentState = NoticeState.STATE_FADING_OUT;

                // フェードアウト開始
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), display1F);
            }
            else
            {
                // 新しいトーストを設定してフェードイン開始
                pendingToastMessage = "";

                messageText.text = message;

                canvasGroup.gameObject.SetActive(true);
                canvasGroup.alpha = 0f;

                // 進捗バーをリセット
                progressBar.rectTransform.sizeDelta = new Vector2(
                    maxProgressWidth,
                    progressBar.rectTransform.sizeDelta.y
                );
                currentProgressStep = 0;

                // 通知音を鳴らす
                audioSource.Play();

                // フェードイン開始
                currentState = NoticeState.STATE_FADING_IN;
                SendCustomEventDelayedSeconds(nameof(FadeInStep), display1F);
            }
        }
#endregion

        /// <summary>
        /// トーストを非表示にするメソッド
        /// </summary>
        public void HideToast()
        {
            if (currentState == NoticeState.STATE_VISIBLE)
            {
                // フェードアウト開始
                currentState = NoticeState.STATE_FADING_OUT;
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), display1F);
            }
        }

        /// <summary>
        /// フェードインの各ステップ
        /// </summary>
        public void FadeInStep()
        {
            if (currentState != NoticeState.STATE_FADING_IN)
                return;

            canvasGroup.alpha += fadeInStepAmount;
            if (canvasGroup.alpha >= 1f)
            {
                canvasGroup.alpha = 1f;
                currentState = NoticeState.STATE_VISIBLE;

                // 進捗バーのアニメーション開始
                StartProgressBar();

                // displayDuration秒後にフェードアウトを開始
                SendCustomEventDelayedSeconds(nameof(HideToast), displayDuration);
            }
            else
            {
                // 次のフェードインステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(FadeInStep), display1F);
            }
        }

        /// <summary>
        /// フェードアウトの各ステップ
        /// </summary>
        public void FadeOutStep()
        {
            if (currentState != NoticeState.STATE_FADING_OUT)
                return;

            canvasGroup.alpha -= fadeOutStepAmount;
            if (canvasGroup.alpha <= 0f)
            {
                canvasGroup.alpha = 0f;
                currentState = NoticeState.STATE_IDLE;
                canvasGroup.gameObject.SetActive(false);

                // 進捗バーをリセット
                progressBar.rectTransform.sizeDelta = new Vector2(
                    maxProgressWidth,
                    progressBar.rectTransform.sizeDelta.y
                );
                currentProgressStep = 0;

                // 保留中のトーストがあれば表示
                if (
                    false == string.IsNullOrEmpty(pendingToastMessage)
                )
                {
                    string message = pendingToastMessage;
                    pendingToastMessage = "";
                    ShowToast(message);
                }
            }
            else
            {
                // 次のフェードアウトステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(FadeOutStep), display1F);
            }
        }

        /// <summary>
        /// 進捗バーのアニメーションを開始する
        /// </summary>
        private void StartProgressBar()
        {
            currentProgressStep = 0;
            SendCustomEventDelayedSeconds(nameof(ProgressBarStep), display1F);
        }

        /// <summary>
        /// 進捗バーの各ステップ
        /// </summary>
        public void ProgressBarStep()
        {
            if (currentState != NoticeState.STATE_VISIBLE)
                return;

            currentProgressStep++;
            if (currentProgressStep >= progressSteps)
            {
                // 進捗バーが完了
                progressBar.rectTransform.sizeDelta = new Vector2(
                    0f,
                    progressBar.rectTransform.sizeDelta.y
                );
            }
            else
            {
                // 進捗バーの幅を減少
                float newWidth = maxProgressWidth - (progressStepAmount * currentProgressStep);
                progressBar.rectTransform.sizeDelta = new Vector2(
                    newWidth,
                    progressBar.rectTransform.sizeDelta.y
                );

                // 次の進捗バーステップをスケジュール
                SendCustomEventDelayedSeconds(nameof(ProgressBarStep), display1F);
            }
        }
    }
}

