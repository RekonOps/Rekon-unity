// SampleBugReporter.cs
// Rekon BasicDemo 샘플
//
// 커스텀 컨텍스트 데이터를 버그 리포트에 추가하는 방법을 보여주는 예제입니다.
// RekonContext.Add() / Remove() / Clear() 정적 API를 사용합니다.
//
// 사용 방법:
//   1. 이 컴포넌트를 Scene의 게임 오브젝트에 추가합니다.
//   2. Play Mode에서 F12를 눌러 버그 리포트를 제출합니다.
//   3. 리포트에 level, score, player_hp 등의 컨텍스트 데이터가 포함됩니다.

using UnityEngine;
using RekonOps.Rekon;

namespace RekonOps.Rekon.Samples
{
    /// <summary>
    /// RekonContext 정적 API 사용 예제.
    /// 게임 상태 변화에 따라 버그 리포트 컨텍스트를 업데이트합니다.
    /// </summary>
    public class SampleBugReporter : MonoBehaviour
    {
        [Header("샘플 데이터")]
        [SerializeField] private int _currentLevel = 1;
        [SerializeField] private int _score = 0;
        [SerializeField] private int _playerHp = 100;

        // ──────────────────────────────────────────────────────────────
        // Unity 생명주기
        // ──────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            // 컴포넌트 활성화 시 초기 컨텍스트 등록
            UpdateContext();
        }

        private void OnDisable()
        {
            // 컴포넌트 비활성화 시 컨텍스트 초기화
            // 주의: RekonContext.Clear()는 모든 컨텍스트를 제거합니다.
            // 여러 컴포넌트가 컨텍스트를 관리한다면 특정 키만 제거하는 것을 권장합니다.
            RekonContext.Remove("level");
            RekonContext.Remove("score");
            RekonContext.Remove("player_hp");
            RekonContext.Remove("scene");
        }

        private void Update()
        {
            // 매 프레임마다 컨텍스트를 업데이트하는 것은 비효율적입니다.
            // 실제 게임에서는 상태가 변할 때만 업데이트하세요.
            // 여기서는 데모 목적으로 매 프레임 갱신합니다.
            UpdateContext();
        }

        // ──────────────────────────────────────────────────────────────
        // 컨텍스트 업데이트
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 게임 상태를 RekonContext에 업데이트합니다.
        /// 버그 리포트 시 이 데이터가 상태 스냅샷에 포함됩니다.
        /// </summary>
        private void UpdateContext()
        {
            RekonContext.Add("level",     _currentLevel.ToString());
            RekonContext.Add("score",     _score.ToString());
            RekonContext.Add("player_hp", _playerHp.ToString());
            RekonContext.Add("scene",
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            RekonContext.Add("frame",     Time.frameCount.ToString());
            RekonContext.Add("time",      Time.time.ToString("F2"));
        }

        // ──────────────────────────────────────────────────────────────
        // 데모용 조작 메서드 (에디터 Inspector 또는 UI 버튼에서 호출 가능)
        // ──────────────────────────────────────────────────────────────

        /// <summary>
        /// 레벨을 증가시키고 컨텍스트를 업데이트합니다.
        /// </summary>
        public void NextLevel()
        {
            _currentLevel++;
            _score = 0;
            UpdateContext();
            Debug.Log($"[SampleBugReporter] 레벨 {_currentLevel} 시작. 컨텍스트 업데이트 완료.");
        }

        /// <summary>
        /// 점수를 증가시킵니다.
        /// </summary>
        public void AddScore(int amount)
        {
            _score += amount;
            RekonContext.Add("score", _score.ToString());
        }

        /// <summary>
        /// 플레이어가 데미지를 받습니다.
        /// </summary>
        public void TakeDamage(int damage)
        {
            _playerHp = Mathf.Max(0, _playerHp - damage);
            RekonContext.Add("player_hp", _playerHp.ToString());

            if (_playerHp <= 0)
            {
                RekonContext.Add("death_cause", "damage");
                Debug.LogWarning("[SampleBugReporter] 플레이어 사망. 버그 리포트 컨텍스트에 death_cause 추가.");
            }
        }
    }
}
