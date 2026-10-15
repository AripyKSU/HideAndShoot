using System;
using System.Text;
using UnityEngine;

namespace HideAndShoot.Lobby
{
    public static class NicknameRules
    {
        public const int MaxCharacters = 20;

        public static readonly string[] Adjectives =
        {
            "행복한", "우울한", "추운", "졸린", "신나는",
            "용감한", "조용한", "느긋한", "배고픈", "부지런한",
            "수줍은", "장난꾸러기", "기쁜", "심심한", "엉뚱한",
            "화난", "따뜻한", "쾌활한", "멋진", "다정한"
        };

        public static readonly string[] Animals =
        {
            "나무늘보", "고릴라", "고양이", "강아지", "토끼",
            "수달", "여우", "호랑이", "사자", "판다",
            "코알라", "펭귄", "부엉이", "너구리", "알파카",
            "하마", "코끼리", "물개", "햄스터", "기린"
        };

        public static string Generate()
        {
            return Adjectives[UnityEngine.Random.Range(0, Adjectives.Length)] + " " +
                   Animals[UnityEngine.Random.Range(0, Animals.Length)];
        }

        public static bool TryNormalize(string text, out string normalized, out string reason)
        {
            normalized = (text ?? string.Empty).Trim();
            reason = string.Empty;
            if (normalized.Length == 0)
            {
                reason = "닉네임을 입력해야 방을 만들거나 참가할 수 있습니다.";
                return false;
            }

            if (normalized.Length > MaxCharacters || Encoding.UTF8.GetByteCount(normalized) > 64)
            {
                reason = "닉네임은 최대 20자(UTF-8 기준 64바이트)까지 가능합니다.";
                return false;
            }

            foreach (char c in normalized)
            {
                if (char.IsControl(c))
                {
                    reason = "닉네임에 제어 문자를 사용할 수 없습니다.";
                    return false;
                }
            }

            return true;
        }
    }
}
