using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TaikoDiveLauncher.Models
{
    public class DifficultyScore
    {
        public string Difficulty { get; set; } = "";
        public int Score { get; set; }
        public double Gauge { get; set; }
        public int Great { get; set; }
        public int Good { get; set; }
        public int Miss { get; set; }
        public int RollCount { get; set; }
        public int MaxCombo { get; set; }
        public string Crown { get; set; } = "NoClear";
        public string ScoreRank { get; set; } = "なし";

        /// <summary>
        /// この難易度がプレイ済みかどうか。
        /// </summary>
        public bool IsPlayed => Score > 0 || Great > 0 || Good > 0 || Miss > 0;
    }

    public class SongScore
    {
        public string SongName { get; set; } = "";
        public List<DifficultyScore> Difficulties { get; set; } = new();

        /// <summary>
        /// プレイ済みの最高難易度のスコアを取得。
        /// </summary>
        public DifficultyScore? BestPlayedDifficulty
        {
            get
            {
                DifficultyScore? best = null;
                foreach (var d in Difficulties)
                {
                    if (d.IsPlayed) best = d;
                }
                return best;
            }
        }
    }

    public static class ScoreDataParser
    {
        private static readonly Encoding ShiftJis;

        static ScoreDataParser()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ShiftJis = Encoding.GetEncoding("shift_jis");
        }

        /// <summary>
        /// ScoreData ディレクトリからプレイヤー名一覧を取得。
        /// </summary>
        public static List<string> GetPlayerNames(string scoreDataDir)
        {
            var players = new List<string>();
            if (!Directory.Exists(scoreDataDir)) return players;

            foreach (var dir in Directory.GetDirectories(scoreDataDir))
            {
                players.Add(Path.GetFileName(dir));
            }
            return players;
        }

        /// <summary>
        /// 指定プレイヤーの全楽曲スコアを読み込む。
        /// </summary>
        public static List<SongScore> LoadPlayerScores(string scoreDataDir, string playerName)
        {
            var scores = new List<SongScore>();
            var playerDir = Path.Combine(scoreDataDir, playerName);
            if (!Directory.Exists(playerDir)) return scores;

            foreach (var file in Directory.GetFiles(playerDir, "*.dat"))
            {
                try
                {
                    var songScore = ParseDatFile(file);
                    if (songScore != null)
                        scores.Add(songScore);
                }
                catch
                {
                    // パース失敗は無視
                }
            }

            // 曲名でソート
            scores.Sort((a, b) => string.Compare(a.SongName, b.SongName, StringComparison.OrdinalIgnoreCase));
            return scores;
        }

        /// <summary>
        /// .dat ファイルを解析して SongScore を返す。
        /// </summary>
        private static SongScore? ParseDatFile(string filePath)
        {
            var content = File.ReadAllText(filePath, ShiftJis);
            var songName = Path.GetFileNameWithoutExtension(filePath);

            var songScore = new SongScore { SongName = songName };

            // 各難易度ブロックを抽出
            string[] difficulties = { "Easy", "Normal", "Hard", "Oni", "Edit" };
            foreach (var diff in difficulties)
            {
                var diffScore = new DifficultyScore { Difficulty = diff };

                // 難易度ブロックを探す
                var diffIndex = content.IndexOf($"\"{diff}\"", StringComparison.Ordinal);
                if (diffIndex < 0) continue;

                var blockStart = content.IndexOf('{', diffIndex);
                var blockEnd = content.IndexOf('}', blockStart);
                if (blockStart < 0 || blockEnd < 0) continue;

                var block = content.Substring(blockStart, blockEnd - blockStart + 1);

                diffScore.Score = ParseInt(block, "Score");
                diffScore.Gauge = ParseDouble(block, "Gauge");
                diffScore.Great = ParseInt(block, "Great");
                diffScore.Good = ParseInt(block, "Good");
                diffScore.Miss = ParseInt(block, "Miss");
                diffScore.RollCount = ParseInt(block, "RollCount");
                diffScore.MaxCombo = ParseInt(block, "MaxCombo");
                diffScore.Crown = ParseString(block, "Crown") ?? "NoClear";
                diffScore.ScoreRank = ParseString(block, "ScoreRank") ?? "なし";

                songScore.Difficulties.Add(diffScore);
            }

            return songScore;
        }

        private static int ParseInt(string block, string key)
        {
            var match = Regex.Match(block, $@"{key}=(.+)");
            if (match.Success && int.TryParse(match.Groups[1].Value.Trim(), out var val))
                return val;
            return 0;
        }

        private static double ParseDouble(string block, string key)
        {
            var match = Regex.Match(block, $@"{key}=(.+)");
            if (match.Success && double.TryParse(match.Groups[1].Value.Trim(), out var val))
                return val;
            return 0;
        }

        private static string? ParseString(string block, string key)
        {
            var match = Regex.Match(block, $@"{key}=(.+)");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
    }
}
