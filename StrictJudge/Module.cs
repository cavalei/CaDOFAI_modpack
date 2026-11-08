using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json;
using Overlayer.ModuleAPI;
using Overlayer.Tag.Core;
using UnityEngine;

namespace Overlayer.Module.StrictJudge
{
    public class StrictJudgeModule : OverlayerModule
    {
        public override string Name => "StrictJudge";
        public override string Author => "dsh";
        public override string Version => "1.3.0";

        public override void OnDispose()
        {
            ReportServer.Stop();
        }

        public override void OnInitialize()
        {
            new Harmony("overlayer.strictjudge").PatchAll(typeof(StrictJudgeModule).Assembly);
            State.ResetAll();
            ReportServer.StartIfHotspot();
            ReportServer.WatchHotspot();
            State.LoadCheckpoints();
            State.LoadRows();
            State.LoadTotals();
            State.WriteExcel();
        }
    }

    public static class State
    {
        public static int Attempt = 0;
        public static readonly List<Row> Rows = new List<Row>();
        public static int HitCount = 0;
        public static float LastOffsetMs;
        public static float LastThresholdMs;
        public static string LastMargin = "";

        public static string CurrentLevel = "";
        public static string CurrentKey = "";
        public static readonly Dictionary<string,int> Attempts = new Dictionary<string,int>();
        public static DateTime SessionStart = DateTime.Now;
        public static string LastRowLevel = "";
        public static string LastRowDifficulty = "";
        internal static bool Busy = false;
        public static float LastBpm = 320f, LastPitch = 1f, LastScale = 1f;
        public static readonly Dictionary<int, int> CheckpointIndex = new Dictionary<int, int>();
        public static int CheckpointCounter = 0;
        private static readonly HashSet<string> StartShown = new HashSet<string>();

        // 每关的检查点表：关卡名 -> (序号 -> 轨道号)
        public static readonly Dictionary<string, Dictionary<int, int>> LevelCheckpoints
            = new Dictionary<string, Dictionary<int, int>>();

        public static readonly Dictionary<string, int> LevelTotals = new Dictionary<string, int>();
        public static string TotalsJsonPath => Path.Combine(OutDir, "严判关卡.json");

        public static void SaveTotals()
        {
            try { File.WriteAllText(TotalsJsonPath, JsonConvert.SerializeObject(LevelTotals, Formatting.Indented), new UTF8Encoding(false)); }
            catch (Exception e) { Debug.LogWarning("[StrictJudge] save totals " + e.Message); }
        }

        public static void LoadTotals()
        {
            try
            {
                if (!File.Exists(TotalsJsonPath)) return;
                var d = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(TotalsJsonPath));
                if (d != null) foreach (var kv in d) LevelTotals[kv.Key] = kv.Value;
            }
            catch (Exception e) { Debug.LogWarning("[StrictJudge] load totals " + e.Message); }
        }

        public static void ResetAll()
        {
            Attempt = 0;
            Rows.Clear();
            HitCount = 0;
            CurrentLevel = "";
            LastRowLevel = "";
            LastRowDifficulty = "";
            CheckpointIndex.Clear();
            CheckpointCounter = 0;
        }

        public static void ResetAttempt()
        {
            HitCount = 0;
            LastOffsetMs = 0; LastThresholdMs = 0; LastMargin = "";
        }

        public class Row
        {
            public int Attempt;
            public string Level;
            public string StrictRange;
            public string CurrentRange;
            public int Tile;
            public float OffsetMs;
            public bool IsFirstOfAttempt;
            public bool Missed;
            public string Note;
            public int CpTile;
            public int CpIndex;
            public string CpText;
            public float Time;
            public bool Died;
            public string Key;
            public string Stamp;
            public string LevelName;
        }

        static bool IsFail(HitMargin m)
        {
            var s = m.ToString();
            return s.Contains("TooLate") || s.Contains("TooEarly") || s.Contains("Fail") || s.Contains("Miss");
        }

        public static float Threshold(Difficulty d, float bpm, float pitch, float scale)
        {
            Busy = true;
            try
            {
                float lo = 0f, hi = 0.5f;
                for (int i = 0; i < 22; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var m = scrMisc.GetHitMarginInSec(d, mid, bpm, pitch, scale);
                    if (IsFail(m)) hi = mid; else lo = mid;
                }
                return lo;
            }
            finally { Busy = false; }
        }

        private static bool _scanned;

        public static void ScanLevelCheckpoints()
        {
            if (_scanned) return;
            if (string.IsNullOrEmpty(CurrentLevel)) return;
            _scanned = true;

            var lm = ADOBase.lm;
            if (lm == null || lm.listFloors == null) return;

            var tiles = new List<int>();
            for (int i = 0; i < lm.listFloors.Count; i++)
            {
                var f = lm.listFloors[i];
                if (f == null) continue;
                bool isCp = false;
                try
                {
                    if (f.onCheckpointEffects != null && f.onCheckpointEffects.Count > 0) isCp = true;
                    else if (f.GetComponentInChildren<ffxCheckpoint>() != null) isCp = true;
                }
                catch { }
                if (isCp) tiles.Add(f.seqID);
            }

            try
            {
                var ld3 = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld3 != null && ld3.angleData != null && !string.IsNullOrEmpty(CurrentKey))
                {
                    LevelTotals[CurrentKey] = ld3.angleData.Count;
                    SaveTotals();
                }
            }
            catch { }

            Debug.Log($"[StrictJudge] scan level '{CurrentLevel}': {tiles.Count} checkpoints, first={(tiles.Count > 0 ? tiles[0].ToString() : "-")}");

            if (tiles.Count == 0) return;

            if (LevelCheckpoints.ContainsKey(CurrentLevel))
            {
                // 表里已有这一关：只重建"轨道号 → 序号"的内存映射，不重复写盘
                CheckpointIndex.Clear();
                CheckpointCounter = 0;
                foreach (var kv in LevelCheckpoints[CurrentLevel])
                {
                    CheckpointIndex[kv.Value] = kv.Key;
                    if (kv.Key > CheckpointCounter) CheckpointCounter = kv.Key;
                }
                return;
            }

            var map = new Dictionary<int, int>();
            for (int i = 0; i < tiles.Count; i++) map[i + 1] = tiles[i];
            LevelCheckpoints[CurrentLevel] = map;

            CheckpointIndex.Clear();
            CheckpointCounter = 0;
            foreach (var kv in map)
            {
                int ord = kv.Key;
                int tl = kv.Value;
                // 轨道号 -> 序号
                CheckpointIndex[tl] = ord;
                if (ord > CheckpointCounter) CheckpointCounter = ord;
            }

            SaveCheckpoints();
            WriteExcel();
        }

        public static void CaptureCheckpoint(out int cpTile, out int cpIndex)
        {
            cpTile = 0; cpIndex = 0;
            try
            {
                cpTile = GCS.checkpointNum;
                if (!CheckpointIndex.TryGetValue(cpTile, out cpIndex)) cpIndex = 0;
            }
            catch { }
        }

        public static readonly List<(int lo, int hi)> DangerZones = new List<(int, int)>();
        public static int DebugTiles;

        public static void RebuildDangerZones()
        {
            DangerZones.Clear();
            var byCp = new Dictionary<int, List<int>>();
            foreach (var r in Rows)
            {
                if (!string.IsNullOrEmpty(CurrentKey) && r.Key != CurrentKey) continue;
                if (r.Tile <= 0) continue;
                if (!byCp.TryGetValue(r.CpIndex, out var list)) { list = new List<int>(); byCp[r.CpIndex] = list; }
                list.Add(r.Tile);
            }
            const int width = 60, minCount = 7;
            foreach (var kv in byCp)
            {
                if (kv.Value.Count < minCount) continue;
                var tiles = kv.Value;
                tiles.Sort();
                int bestStart = tiles[0], bestCount = 0, bestSpread = int.MaxValue;
                foreach (var s0 in tiles)
                {
                    int cnt = 0, mn = int.MaxValue, mx = int.MinValue;
                    foreach (var v in tiles)
                    {
                        if (v >= s0 && v <= s0 + width)
                        {
                            cnt++;
                            if (v < mn) mn = v;
                            if (v > mx) mx = v;
                        }
                    }
                    if (cnt == 0) continue;
                    int spread = mx - mn;
                    if (cnt > bestCount || (cnt == bestCount && spread < bestSpread))
                    {
                        bestCount = cnt; bestStart = mn; bestSpread = spread;
                    }
                }
                DangerZones.Add((bestStart - 5, bestStart + width + 5));
            }
            DangerZones.Sort((a, b) => a.lo.CompareTo(b.lo));
            var zs = string.Join(",", DangerZones.ConvertAll(z => z.lo + "-" + z.hi));
            Debug.Log($"[StrictJudge] danger zones: {DangerZones.Count} [{zs}] (rows={Rows.Count})");
        }

        public static bool InDanger(int tile)
        {
            foreach (var z in DangerZones)
                if (tile >= z.lo && tile <= z.hi) return true;
            return false;
        }

        public static Dictionary<int,int> CountByCheckpoint(List<Row> rows, string key)
        {
            var d = new Dictionary<int,int>();
            foreach (var r in rows)
            {
                if (!string.IsNullOrEmpty(key) && r.Key != key) continue;
                int k = r.CpIndex;
                if (!d.ContainsKey(k)) d[k] = 0;
                d[k]++;
            }
            return d;
        }

        public static string BuildKey()
        {
            string song = CurrentLevel ?? "";
            string author = "";
            string bpm = "";
            try
            {
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld != null)
                {
                    author = ld.author ?? "";
                    bpm = Math.Round(ld.bpm).ToString();
                }
            }
            catch { }
            return song + "|" + author + "|" + bpm;
        }

        private static int MaxAttemptForKey(string key)
        {
            int max = 0;
            foreach (var r in Rows)
                if (r.Key == key && r.Attempt > max) max = r.Attempt;
            return max;
        }

        public static void EnsureAttempt()
        {
            string key = CurrentKey;
            if (string.IsNullOrEmpty(key)) return;
            if (!Attempts.TryGetValue(key, out int a))
            {
                a = MaxAttemptForKey(key) + 1;
                Attempts[key] = a;
            }
            Attempt = a;
        }

        private static string CpCellText(int cpIndex)
        {
            if (cpIndex > 0) return "#" + cpIndex;
            string lv = CurrentLevel ?? "";
            if (StartShown.Contains(lv)) return "起点";
            StartShown.Add(lv);
            return "";
        }

        private static bool FirstOfAttempt()
        {
            foreach (var r in Rows)
                if (r.Attempt == Attempt) return false;
            return true;
        }

        public static void Record(double timeDiff, HitMargin actual, float bpm, float pitch, float scale, int tile)
        {
            LastBpm = bpm; LastPitch = pitch; LastScale = scale;
            float strict = Threshold(Difficulty.Strict, bpm, pitch, scale);
            float current = Threshold(GCS.difficulty, bpm, pitch, scale);

            HitCount++;
            LastOffsetMs = (float)Math.Abs(timeDiff) * 1000f;
            LastThresholdMs = strict * 1000f;
            LastMargin = actual.ToString();

            if (!(timeDiff > strict)) return;

            string diffName = GCS.difficulty.ToString();
            bool needHeader = FirstOfAttempt() || CurrentLevel != LastRowLevel || diffName != LastRowDifficulty;

            EnsureAttempt();
            if (Attempt == 0) Attempt = 1;

            foreach (var r in Rows)
                if (r.Attempt == Attempt && r.Key == CurrentKey && r.Tile == tile) return;

            int cpT, cpI;
            CaptureCheckpoint(out cpT, out cpI);
            if (cpT > 0 && tile >= 0 && tile <= cpT + 5) return;   // 复活点后 5 格内不记

            Rows.Add(new Row
            {
                CpTile = cpT,
                CpIndex = cpI,
                CpText = CpCellText(cpI),
                Key = CurrentKey,
                LevelName = CurrentLevel,
                Stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Time = Time.time,
                Attempt = Attempt,
                Level = needHeader ? CurrentLevel : "",
                StrictRange = needHeader ? "\u00b1" + (strict * 1000f).ToString("0.00") + "ms" : "",
                CurrentRange = needHeader ? "\u00b1" + (current * 1000f).ToString("0.00") + "ms" : "",
                Tile = tile,
                OffsetMs = LastOffsetMs,
                IsFirstOfAttempt = FirstOfAttempt(),
            });

            LastRowLevel = CurrentLevel;
            LastRowDifficulty = diffName;

            SaveRows();
            WriteExcel();
            Debug.Log($"[StrictJudge] row a{Attempt} tile={tile} off={LastOffsetMs:0.00} strict={strict * 1000f:0.00} cur={current * 1000f:0.00}");
        }

        public static void RecordMiss(int tile)
        {
            EnsureAttempt();
            if (Attempt == 0) Attempt = 1;
            foreach (var r in Rows)
                if (r.Attempt == Attempt && r.Key == CurrentKey && r.Tile == tile) return;

            float strict = Threshold(Difficulty.Strict, LastBpm, LastPitch, LastScale);
            float current = Threshold(GCS.difficulty, LastBpm, LastPitch, LastScale);

            string diffName = GCS.difficulty.ToString();
            bool needHeader = FirstOfAttempt() || CurrentLevel != LastRowLevel || diffName != LastRowDifficulty;

            int cpT, cpI;
            CaptureCheckpoint(out cpT, out cpI);
            if (cpT > 0 && tile >= 0 && tile <= cpT + 5) return;   // 复活点后 5 格内不记

            Rows.Add(new Row
            {
                CpTile = cpT,
                CpIndex = cpI,
                CpText = CpCellText(cpI),
                Key = CurrentKey,
                LevelName = CurrentLevel,
                Stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Time = Time.time,
                Attempt = Attempt,
                Level = needHeader ? CurrentLevel : "",
                StrictRange = needHeader ? "\u00b1" + (strict * 1000f).ToString("0.00") + "ms" : "",
                CurrentRange = needHeader ? "\u00b1" + (current * 1000f).ToString("0.00") + "ms" : "",
                Tile = tile,
                OffsetMs = 0f,
                IsFirstOfAttempt = FirstOfAttempt(),
                Missed = true,
            });

            LastRowLevel = CurrentLevel;
            LastRowDifficulty = diffName;
            SaveRows();
            WriteExcel();
            Debug.Log($"[StrictJudge] miss row a{Attempt} tile={tile}");
        }

        public static void RecordPass(int tile)
        {
            if (HitCount <= 0) return;
            EnsureAttempt();
            if (Attempt == 0) Attempt = 1;
            if (!FirstOfAttempt()) return;   // 本局已有超窗，就不再加"安全通过"

            float strict = Threshold(Difficulty.Strict, LastBpm, LastPitch, LastScale);
            float current = Threshold(GCS.difficulty, LastBpm, LastPitch, LastScale);
            string diffName = GCS.difficulty.ToString();
            bool needHeader = FirstOfAttempt() || CurrentLevel != LastRowLevel || diffName != LastRowDifficulty;

            int cpT, cpI;
            CaptureCheckpoint(out cpT, out cpI);

            Rows.Add(new Row
            {
                CpTile = cpT,
                CpIndex = cpI,
                CpText = CpCellText(cpI),
                Key = CurrentKey,
                LevelName = CurrentLevel,
                Stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Time = Time.time,
                Attempt = Attempt,
                Level = needHeader ? CurrentLevel : "",
                StrictRange = needHeader ? "\u00b1" + (strict * 1000f).ToString("0.00") + "ms" : "",
                CurrentRange = needHeader ? "\u00b1" + (current * 1000f).ToString("0.00") + "ms" : "",
                Tile = tile,
                Note = "\u901a\u5173\uff08\u65e0\u8d85\u7a97\uff09",
                IsFirstOfAttempt = true,
            });

            LastRowLevel = CurrentLevel;
            LastRowDifficulty = diffName;
            SaveRows();
            WriteExcel();
            Debug.Log($"[StrictJudge] pass row a{Attempt} tile={tile}");
        }

        public static void ResetScanForLevel()
        {
            _scanned = false;
        }

        public static void MarkDeath()
        {
            float now = Time.time;
            foreach (var r in Rows)
            {
                if (r.Attempt != Attempt) continue;
                if (now - r.Time <= 0.8f && (r.Missed || r.Tile >= 0))
                {
                    r.Died = true;
                }
            }
            WriteExcel();
        }

        public static int CurrentAttemptCount() => CurrentAttemptRows().Count;

        public static void BeginRun()
        {
            if (string.IsNullOrEmpty(CurrentKey)) return;
            bool hasRowsThisAttempt = false;
            foreach (var r in Rows)
            {
                if (r.Key == CurrentKey && r.Attempt == Attempt) { hasRowsThisAttempt = true; break; }
            }
            if (hasRowsThisAttempt)
            {
                Attempt++;
                Attempts[CurrentKey] = Attempt;
                ResetAttempt();
                StartShown.Clear();
                Debug.Log($"[StrictJudge] 重新开始，进入计次 #{Attempt}");
            }
        }

        public static List<Row> CurrentAttemptRows()
        {
            var list = new List<Row>();
            foreach (var r in Rows)
            {
                if (!string.IsNullOrEmpty(CurrentKey) && r.Key != CurrentKey) continue;
                if (r.Attempt != Attempt) continue;
                list.Add(r);
            }
            return list;
        }

        public static void FinalizeAttempt()
        {
            Debug.Log($"[StrictJudge] settle: rows={Rows.Count} hits={HitCount} attempt={Attempt}");
            if (Rows.Count == 0 && HitCount == 0) return;
            Attempt++;
            if (!string.IsNullOrEmpty(CurrentKey)) Attempts[CurrentKey] = Attempt;
            ResetAttempt();
            SaveRows();
        }

        // ---- 检查点登记 ----
        public static void RegisterCheckpoint(int tile)
        {
            // 扫描关卡时已确定检查点表；运行期只作兜底，避免出现第 14 个这种多出来的点
            if (tile <= 0) return;
            if (string.IsNullOrEmpty(CurrentLevel)) return;

            if (LevelCheckpoints.TryGetValue(CurrentLevel, out var existing) && existing.Count > 0)
                return;

            if (CheckpointIndex.ContainsKey(tile)) return;
            CheckpointCounter++;
            CheckpointIndex[tile] = CheckpointCounter;

            var map = new Dictionary<int, int> { [CheckpointCounter] = tile };
            LevelCheckpoints[CurrentLevel] = map;
            SaveCheckpoints();
            WriteExcel();
        }

        public static string OutDir
        {
            get
            {
                try
                {
                    var data = Application.dataPath;
                    return Directory.GetParent(data).FullName;
                }
                catch { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
            }
        }

        public static string RowsJsonPath => Path.Combine(OutDir, "严判记录.json");

        public static void SaveRows()
        {
            try { File.WriteAllText(RowsJsonPath, JsonConvert.SerializeObject(Rows, Formatting.None), new UTF8Encoding(false)); }
            catch (Exception e) { Debug.LogWarning("[StrictJudge] save rows " + e.Message); }
        }

        public static void LoadRows()
        {
            try
            {
                if (!File.Exists(RowsJsonPath)) return;
                var list = JsonConvert.DeserializeObject<List<Row>>(File.ReadAllText(RowsJsonPath));
                if (list == null) return;
                Rows.Clear();
                Rows.AddRange(list);
                if (Rows.Count > 0)
                {
                    var last = Rows[Rows.Count - 1];
                    if (!string.IsNullOrEmpty(last.Key))
                    {
                        CurrentKey = last.Key;
                        CurrentLevel = !string.IsNullOrEmpty(last.LevelName) ? last.LevelName : (last.Level ?? "");
                    }
                }
                EnsureAttempt();
                Debug.Log($"[StrictJudge] loaded {Rows.Count} rows, key={CurrentKey}, next attempt={Attempt}");
            }
            catch (Exception e) { Debug.LogWarning("[StrictJudge] load rows " + e.Message); }
        }

        public static string CpJsonPath => Path.Combine(OutDir, "\u4e25\u5224\u68c0\u67e5\u70b9.json");

        public static void SaveCheckpoints()
        {
            try
            {
                var data = new Dictionary<string, Dictionary<int, int>>();
                foreach (var kv in LevelCheckpoints) data[kv.Key] = kv.Value;
                File.WriteAllText(CpJsonPath, JsonConvert.SerializeObject(data, Formatting.Indented), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogError("[StrictJudge] save cp " + e); }
        }

        public static void LoadCheckpoints()
        {
            try
            {
                if (!File.Exists(CpJsonPath)) return;
                var data = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<int, int>>>(File.ReadAllText(CpJsonPath));
                if (data == null) return;
                foreach (var kv in data) LevelCheckpoints[kv.Key] = kv.Value;
            }
            catch (Exception e) { Debug.LogError("[StrictJudge] load cp " + e); }
        }

        public static string ExcelPath => Path.Combine(OutDir, "\u4e25\u5224\u5931\u8d25\u8bb0\u5f55.xlsx");

        public static string HtmlPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "\u4e25\u5224\u5931\u8d25\u8bb0\u5f55.html");

        public static void WriteHtml()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><meta http-equiv=\"refresh\" content=\"2\">");
                sb.Append("<title>\u4e25\u5224\u5931\u8d25\u8bb0\u5f55</title>");
                sb.Append("<style>body{font-family:system-ui,'Segoe UI',sans-serif;background:#1b1b1f;color:#e8e8ea;margin:24px}");
                sb.Append("table{border-collapse:collapse;margin-bottom:28px}th,td{border:1px solid #3a3a42;padding:6px 12px;font-size:14px}");
                sb.Append("th{background:#2a2a32;text-align:left}h2{font-weight:600;font-size:16px;margin:18px 0 8px}");
                sb.Append("tr:nth-child(even){background:#232329}</style></head><body>");

                int curAttempt = Attempt;

                // 收集本关所有"计次"及其行数
                var attemptRows = new Dictionary<int, List<Row>>();
                foreach (var r in Rows)
                {
                    if (!string.IsNullOrEmpty(CurrentKey) && r.Key != CurrentKey) continue;
                    if (!attemptRows.TryGetValue(r.Attempt, out var list))
                    {
                        list = new List<Row>();
                        attemptRows[r.Attempt] = list;
                    }
                    list.Add(r);
                }

                int curCount = attemptRows.TryGetValue(curAttempt, out var curList) ? curList.Count : 0;
                int take = curCount > 10 ? 1 : (curCount > 5 ? 4 : 10);

                var attemptKeys = new List<int>(attemptRows.Keys);
                attemptKeys.Sort((a, b) => b.CompareTo(a));
                if (attemptKeys.Count > take)
                    attemptKeys = attemptKeys.GetRange(0, take);

                sb.Append($"<p style=\"color:#888;font-size:12px;margin:0 0 8px\">\u663e\u793a\u6700\u8fd1 {attemptKeys.Count} \u6b21\u8bb0\u5f55</p>");
                sb.Append($"<p style=\"color:#9ad0ff;font-size:14px;margin:0 0 10px\">\u5f53\u524d\u6b63\u5728\u6e38\u73a9\uff1a{System.Net.WebUtility.HtmlEncode(CurrentLevel)}</p>");
                sb.Append("<table><tr><th>\u8ba1\u6b21</th><th>\u590d\u6d3b\u70b9</th><th>\u4e25\u5224\u8303\u56f4</th><th>\u5f53\u524d\u8303\u56f4</th><th>\u8f68\u9053</th><th>\u504f\u79fb</th><th>\u662f\u5426\u6b7b\u4ea1</th></tr>");

                bool anyShown = false;
                foreach (var att in attemptKeys)
                {
                    var rowsOfAtt = attemptRows[att];
                    if (rowsOfAtt.Count == 0) continue;
                    anyShown = true;
                    foreach (var r in rowsOfAtt)
                    {
                        sb.Append("<tr>");
                        sb.Append($"<td>{(r.IsFirstOfAttempt ? "#" + r.Attempt : "")}</td>");
                        sb.Append($"<td>{r.CpText}</td>");
                        sb.Append($"<td>{r.StrictRange}</td><td>{r.CurrentRange}</td>");
                        sb.Append($"<td>{r.Tile}</td>");
                        string off = r.Note != null ? r.Note : (r.Missed ? "\u672a\u51fb\u4e2d" : r.OffsetMs.ToString("0.00") + "ms");
                        sb.Append($"<td>{off}</td>");
                        sb.Append($"<td>{(r.Died ? "\u6b7b\u4ea1" : "\u672a\u6b7b\u4ea1")}</td>");
                        sb.Append("</tr>");
                    }
                }
                sb.Append("</table>");

                var cpCount = CountByCheckpoint(Rows, CurrentKey);
                var ranked = new List<KeyValuePair<int,int>>(cpCount);
                ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

                var qualified = new List<KeyValuePair<int,int>>();
                foreach (var kv in ranked)
                    if (kv.Value >= 7) qualified.Add(kv);

                Dictionary<int,int> cpmapS = null;
                LevelCheckpoints.TryGetValue(CurrentLevel ?? "", out cpmapS);
                var cpKeysS = cpmapS != null ? new List<int>(cpmapS.Keys) : new List<int>();
                cpKeysS.Sort();

                if (qualified.Count > 0)
                {
                    var sbTop = new StringBuilder();
                    foreach (var kv in qualified)
                    {
                        if (sbTop.Length > 0) sbTop.Append(" \u00b7 ");
                        sbTop.Append("\u590d\u6d3b\u70b9#" + kv.Key + " " + kv.Value + " \u6b21");
                    }
                    sb.Append($"<p style=\"color:#ffb454;font-size:13px;margin:0 0 6px\">\u6b7b\u5f97\u6700\u591a\uff1a{sbTop}</p>");

                    var zoneItems = new List<string>();
                    foreach (var kv in qualified)
                    {
                        var tilesOfZone = new List<int>();
                        foreach (var r in Rows)
                        {
                            if (!string.IsNullOrEmpty(CurrentKey) && r.Key != CurrentKey) continue;
                            if (r.CpIndex != kv.Key) continue;
                            if (r.Tile > 0) tilesOfZone.Add(r.Tile);
                        }
                        if (tilesOfZone.Count == 0) continue;
                        tilesOfZone.Sort();
                        const int width = 60;
                        int bestStart = tilesOfZone[0], bestCount = 0;
                        foreach (var s0 in tilesOfZone)
                        {
                            int cnt = 0;
                            foreach (var v in tilesOfZone)
                                if (v >= s0 && v <= s0 + width) cnt++;
                            if (cnt > bestCount) { bestCount = cnt; bestStart = s0; }
                        }
                        zoneItems.Add($"\u590d\u6d3b\u70b9#{kv.Key}\uff1a\u7b2c{bestStart - 5}\u53f7\u8f68\u9053 ~ \u7b2c{bestStart + width + 5}\u53f7\u8f68\u9053\uff08\u533a\u95f4\u5185 {bestCount} \u6b21\uff09");
                    }
sb.Append("<p style=\"color:#ff8a8a;font-size:13px;margin:0 0 4px\">\u6613\u6b7b\u4ea1\u533a\u95f4\uff1a</p>");
                    sb.Append("<ol style=\"margin:0 0 14px 22px;padding:0;color:#ff8a8a;font-size:13px;line-height:1.7\">");
                    foreach (var item in zoneItems)
                        sb.Append($"<li>{item}</li>");
                    sb.Append("</ol>");
                }
                else
                {
                    sb.Append("<p style=\"font-size:0\">&nbsp;</p>");
                }

                // 失败分布折线图（内联 SVG）
                if (anyShown)
                {
                    var allTiles = new List<int>();
                    foreach (var r in Rows)
                    {
                        if (!string.IsNullOrEmpty(CurrentKey) && r.Key != CurrentKey) continue;
                        if (r.Tile > 0) allTiles.Add(r.Tile);
                    }
                    if (allTiles.Count > 1)
                    {
                        int maxTile = 0;
                        foreach (var v in allTiles) if (v > maxTile) maxTile = v;
                        int levelTotal;
                        if (!string.IsNullOrEmpty(CurrentKey) && LevelTotals.TryGetValue(CurrentKey, out levelTotal) && levelTotal > maxTile)
                            maxTile = levelTotal - 1;
                        int bins = 80;
                        int step = Math.Max(1, (int)Math.Ceiling((maxTile + 1) / (double)bins));
                        var counts = new int[bins];
                        foreach (var v in allTiles)
                        {
                            int b = v / step;
                            if (b >= 0 && b < bins) counts[b]++;
                        }
                        int peak = 0;
                        foreach (var c0 in counts) if (c0 > peak) peak = c0;
                        if (peak == 0) peak = 1;

                        const int w = 900, h = 340, padL = 40, padB = 140, padT = 10;
                        int plotW = w - padL - 10, plotH = h - padB - padT;
                        var dots = new StringBuilder();
                        for (int i = 0; i < bins; i++)
                        {
                            double x = padL + plotW * (i + 0.5) / bins;
                            double y = padT + plotH * (1 - counts[i] / (double)peak);
                            dots.Append($"{x:0.#},{y:0.#} ");
                        }

                        sb.Append("<h2 style=\"margin-top:26px\">\u5931\u8d25\u5206\u5e03</h2>");
                        sb.Append($"<svg width=\"100%\" viewBox=\"0 0 {w} {h}\" style=\"background:#232329;border-radius:6px\">");

                        foreach (var z in DangerZones)
                        {
                            int b0 = Math.Max(0, z.lo / step);
                            int b1 = Math.Min(bins - 1, z.hi / step);
                            double x0 = padL + plotW * b0 / (double)bins;
                            double x1 = padL + plotW * (b1 + 1) / (double)bins;
                            sb.Append($"<rect x=\"{x0:0.#}\" y=\"{padT}\" width=\"{x1 - x0:0.#}\" height=\"{plotH}\" fill=\"#ff4444\" opacity=\"0.16\"/>");
                        }

                        sb.Append($"<line x1=\"{padL}\" y1=\"{padT + plotH}\" x2=\"{padL + plotW}\" y2=\"{padT + plotH}\" stroke=\"#555\"/>");
                        sb.Append($"<polyline fill=\"none\" stroke=\"#7bd88f\" stroke-width=\"2\" points=\"{dots}\"/>");
                        for (int i = 0; i < bins; i++)
                        {
                            double x = padL + plotW * (i + 0.5) / bins;
                            double y = padT + plotH * (1 - counts[i] / (double)peak);
                            if (counts[i] > 0) sb.Append($"<circle cx=\"{x:0.#}\" cy=\"{y:0.#}\" r=\"2.2\" fill=\"#7bd88f\"/>");
                        }
                        // 刻度：0、25%、50%、75%、100%
                        // 每 5% 一个刻度短线（不向上延伸），刻度数字橙色
                        double axisBase = padT + plotH;
                        for (int k = 0; k <= 20; k++)
                        {
                            double x = padL + plotW * k / 20.0;
                            double tickLabelY = axisBase + 40;
                            sb.Append($"<line x1=\"{x:0.#}\" y1=\"{axisBase:0.#}\" x2=\"{x:0.#}\" y2=\"{axisBase + 5:0.#}\" stroke=\"#4a4a56\"/>");
                            int tileMark = (int)Math.Round(maxTile * k / 20.0);
                            sb.Append($"<text x=\"{x:0.#}\" y=\"{tickLabelY:0.#}\" fill=\"#e0a040\" font-size=\"10\" text-anchor=\"middle\">{tileMark}</text>");
                        }
                        // 检查点竖线
                        Dictionary<int,int> cpmap2 = null;
                        if (!string.IsNullOrEmpty(CurrentLevel)) LevelCheckpoints.TryGetValue(CurrentLevel, out cpmap2);
                        if (cpmap2 != null)
                        {
                            var cpList = new List<KeyValuePair<int,int>>(cpmap2);
                            cpList.Sort((a, b) => a.Value.CompareTo(b.Value));
                            int row = 0;
                            double lastX = -999;
                            foreach (var kv in cpList)
                            {
                                double x = padL + plotW * kv.Value / (double)(maxTile + 1);
                                sb.Append($"<line x1=\"{x:0.#}\" y1=\"{padT}\" x2=\"{x:0.#}\" y2=\"{padT + plotH}\" stroke=\"#5a5a6a\" stroke-dasharray=\"2,4\"/>");
                                if (x - lastX < 46) row = 1 - row; else row = 0;   // 太近就换到另一行
                                double ly = padT + 11 + row * 12;
                                sb.Append($"<text x=\"{x + 2:0.#}\" y=\"{ly:0.#}\" fill=\"#8a8a99\" font-size=\"10\">#{kv.Key}</text>");

                                // 横轴下方标注该检查点的轨道号，拥挤时用引导线错开
                                double axisY = padT + plotH;
                                double labelY = axisY + 16 + row * 12;
                                sb.Append($"<line x1=\"{x:0.#}\" y1=\"{axisY:0.#}\" x2=\"{x:0.#}\" y2=\"{labelY - 9:0.#}\" stroke=\"#4a4a56\"/>");
                                sb.Append($"<text x=\"{x:0.#}\" y=\"{labelY:0.#}\" fill=\"#9a9aa8\" font-size=\"10\" text-anchor=\"middle\">{kv.Value}</text>");
                                lastX = x;
                            }
                        }
                        // 横向网格：每 5 次一条；峰值 >20 时每 10 标数字，5 的线保留但更淡
                        bool labelEvery10 = peak > 20;
                        for (int g = 5; g <= peak - 2; g += 5)
                        {
                            double gy = padT + plotH * (1 - g / (double)peak);
                            bool major = !labelEvery10 || (g % 10 == 0);
                            string stroke = major ? "#2f2f38" : "#26262e";
                            sb.Append($"<line x1=\"{padL}\" y1=\"{gy:0.#}\" x2=\"{padL + plotW}\" y2=\"{gy:0.#}\" stroke=\"{stroke}\"/>");
                            if (major)
                                sb.Append($"<text x=\"{padL - 6}\" y=\"{gy + 4:0.#}\" fill=\"#8a8a99\" font-size=\"10\" text-anchor=\"end\">{g}</text>");
                        }
                        sb.Append($"<text x=\"4\" y=\"{padT + 12}\" fill=\"#888\" font-size=\"12\">{peak}</text>");
                        sb.Append("</svg>");
                        sb.Append("<p style=\"color:#888;font-size:12px;margin:6px 0 0\">\u6a2a\u8f74\uff1a\u8f68\u9053\u53f7 \u00b7 \u7eb5\u8f74\uff1a\u5931\u8d25\u6b21\u6570 \u00b7 \u7ea2\u8272\u5e26\uff1a\u6613\u6b7b\u4ea1\u533a\u95f4</p>");
                    }
                }
                sb.Append("<h2>\u68c0\u67e5\u70b9</h2><table><tr><th>\u5173\u5361\u540d</th><th>\u590d\u6d3b\u70b9\u5e8f\u53f7</th><th>\u8f68\u9053\u53f7</th><th>\u8be5\u6bb5\u5931\u8d25\u6b21\u6570</th></tr>");
                foreach (var lv in LevelCheckpoints.Keys)
                {
                    if (!string.IsNullOrEmpty(CurrentLevel) && lv != CurrentLevel) continue;
                    var map = LevelCheckpoints[lv];
                    var keys = new List<int>(map.Keys);
                    keys.Sort();
                    bool first = true;
                    int c0s; cpCount.TryGetValue(0, out c0s);
                    sb.Append("<tr>");
                    sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(lv)}</td>");
                    sb.Append("<td>#0 \u8d77\u70b9</td><td></td>");
                    sb.Append($"<td>{c0s}</td></tr>");
                    first = false;

                    foreach (var k in keys)
                    {
                        sb.Append("<tr>");
                        sb.Append($"<td>{(first ? System.Net.WebUtility.HtmlEncode(lv) : "")}</td>");
                        sb.Append($"<td>#{k}</td><td>{map[k]}</td>");
                        int c1; cpCount.TryGetValue(k, out c1);
                        string style = (ranked.Count > 0 && c1 > 0 && c1 == ranked[0].Value) ? " style=\"color:#ff6b6b;font-weight:600\"" : "";
                        sb.Append($"<td{style}>{c1}</td>");
                        sb.Append("</tr>");
                        first = false;
                    }
                }
                sb.Append("</table>");
                sb.Append($"<p style=\"color:#888;font-size:12px\">\u6570\u636e\u751f\u6210\u4e8e {DateTime.Now:HH:mm:ss} \u00b7 \u9875\u9762\u6bcf 2 \u79d2\u81ea\u52a8\u91cd\u8f7d \u00b7 \u73b0\u5728 <b id=\"clk\" style=\"color:#7bd88f\">--:--:--</b></p>");
                sb.Append("<script>function t(){var d=new Date();var p=function(n){return n<10?'0'+n:n};document.getElementById('clk').textContent=p(d.getHours())+':'+p(d.getMinutes())+':'+p(d.getSeconds())}t();setInterval(t,1000);</script>");
                sb.Append("</body></html>");

                File.WriteAllText(HtmlPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning("[StrictJudge] html " + e.Message); }
        }

        /// <summary>Excel 打开时会生成 ~$ 锁文件，也可能独占句柄；两者都视为“正在看表”。</summary>
        private static bool ExcelLocked(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                var name = Path.GetFileName(path);
                if (File.Exists(Path.Combine(dir, "~$" + name))) return true;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException) { return true; }
            catch { return true; }
        }

        public static void WriteExcel()
        {
            RebuildDangerZones();
            WriteHtml();

            // 正被 Excel 打开时不要动主文件：反复删除/重建会让 Excel 读到半截文件而崩溃
            if (ExcelLocked(ExcelPath))
            {
                try { Xlsx.Write(Path.Combine(OutDir, "严判失败记录_副本.xlsx"), Rows, LevelCheckpoints); }
                catch { }
                return;
            }

            try
            {
                Xlsx.Write(ExcelPath, Rows, LevelCheckpoints);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StrictJudge] 主表格被占用，改写到备用文件: " + e.Message);
            }

            try
            {
                string alt = Path.Combine(OutDir, "严判失败记录_副本.xlsx");
                Xlsx.Write(alt, Rows, LevelCheckpoints);
            }
            catch (Exception e2)
            {
                Debug.LogWarning("[StrictJudge] 备用文件也失败: " + e2.Message);
            }
        }
    }

    internal static class Xlsx
    {
        public static void Write(string path, List<State.Row> rows, Dictionary<string, Dictionary<int, int>> cps)
        {
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Add(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"\u5931\u8d25\u8bb0\u5f55\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"\u68c0\u67e5\u70b9\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Add(zip, "xl/_rels/workbook.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                Add(zip, "xl/worksheets/sheet1.xml", BuildSheet(rows));
                Add(zip, "xl/worksheets/sheet2.xml", BuildCpSheet(cps));
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            var e = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest);
            using (var s = e.Open())
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                s.Write(bytes, 0, bytes.Length);
            }
        }

        private static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string Cell(int col, string text, int rowIdx)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return $"<c r=\"{Col(col)}{rowIdx}\" t=\"inlineStr\"><is><t>{Esc(text)}</t></is></c>";
        }

        private static string NumCell(int col, int value, int rowIdx)
        {
            return $"<c r=\"{Col(col)}{rowIdx}\"><v>{value}</v></c>";
        }

        private static string Col(int i)
        {
            string s = "";
            while (i > 0)
            {
                int m = (i - 1) % 26;
                s = (char)('A' + m) + s;
                i = (i - 1) / 26;
            }
            return s;
        }

        private static string Wrap(string inner)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + inner + "</sheetData></worksheet>";
        }

        private static string BuildSheet(List<State.Row> rows)
        {
            var sb = new StringBuilder();
            string[] head = { "\u5931\u8d25\u8bb0\u5f55\u8ba1\u6b21", "\u5173\u5361\u540d", "\u590d\u6d3b\u70b9", "\u4e25\u5224\u6709\u6548\u8303\u56f4", "\u5f53\u524d\u5224\u65ad\u6709\u6548\u8303\u56f4", "\u5931\u8d25\u8f68\u9053\u4f4d\u7f6e", "\u5931\u8d25\u65f6\u504f\u79fb", "\u662f\u5426\u6b7b\u4ea1" };
            sb.Append("<row r=\"1\">");
            for (int i = 0; i < head.Length; i++) sb.Append(Cell(i + 1, head[i], 1));
            sb.Append("</row>");

            int r = 2;
            foreach (var row in rows)
            {
                sb.Append($"<row r=\"{r}\">");
                if (row.IsFirstOfAttempt) sb.Append(Cell(1, "#" + row.Attempt, r));
                sb.Append(Cell(2, row.Level, r));
                sb.Append(Cell(3, row.CpText, r));
                sb.Append(Cell(4, row.StrictRange, r));
                sb.Append(Cell(5, row.CurrentRange, r));
                sb.Append(NumCell(6, row.Tile, r));
                string off = row.Note != null ? row.Note : (row.Missed ? "\u672a\u51fb\u4e2d" : row.OffsetMs.ToString("0.00") + "ms");
                sb.Append(Cell(7, off, r));
                sb.Append(Cell(8, row.Died ? "\u6b7b\u4ea1" : "\u672a\u6b7b\u4ea1", r));
                sb.Append("</row>");
                r++;
            }
            return Wrap(sb.ToString());
        }


        private static string BuildCpSheet(Dictionary<string, Dictionary<int, int>> cps)
        {
            var sb = new StringBuilder();
            string[] head = { "\u5173\u5361\u540d", "\u590d\u6d3b\u70b9\u5e8f\u53f7", "\u68c0\u67e5\u70b9\u6240\u5728\u8f68\u9053\u53f7", "\u8be5\u6bb5\u5931\u8d25\u6b21\u6570" };
            sb.Append("<row r=\"1\">");
            for (int i = 0; i < head.Length; i++) sb.Append(Cell(i + 1, head[i], 1));
            sb.Append("</row>");

            int r = 2;
            foreach (var level in cps.Keys)
            {
                var map = cps[level];
                var keys = new List<int>(map.Keys);
                keys.Sort();
                bool firstOfLevel = true;
                var counts = State.CountByCheckpoint(State.Rows, null);
                foreach (var k in keys)
                {
                    sb.Append($"<row r=\"{r}\">");
                    if (firstOfLevel) sb.Append(Cell(1, level, r));
                    sb.Append(Cell(2, "#" + k, r));
                    sb.Append(NumCell(3, map[k], r));
                    int c0;
                    counts.TryGetValue(k, out c0);
                    sb.Append(NumCell(4, c0, r));
                    sb.Append("</row>");
                    r++;
                    firstOfLevel = false;
                }
            }
            return Wrap(sb.ToString());
        }
    }

    public static class Tags
    {
        [Tag(Name = "SJ_Status", Desc = "严判状态")]
        public static string Status => (State.CurrentAttemptCount() == 0 || State.HitCount == 0) ? "" : "严判会死";

        [Tag(Name = "SJ_DeathInfo", Desc = "最近一次超窗详情")]
        public static string DeathInfo
        {
            get
            {
                var rows = State.CurrentAttemptRows();
                if (rows.Count == 0) return "";
                var r = rows[rows.Count - 1];
                return $"轨道{r.Tile} {r.OffsetMs:0.0}ms";
            }
        }

        [Tag(Name = "SJ_LateCount", Desc = "超窗次数")]
        public static string LateCount => State.Rows.Count.ToString();

        [Tag(Name = "SJ_Hits", Desc = "已记录击打数")]
        public static string Hits => State.HitCount.ToString();

        [Tag(Name = "SJ_LastOffset", Desc = "最近一次偏移(ms)")]
        public static string LastOffset => State.LastOffsetMs.ToString("0.0");

        [Tag(Name = "SJ_InRange", Desc = "是否处于易死亡区间")]
        public static string InRange
        {
            get
            {
                int tile = -1;
                try
                {
                    var ctrl = scrController.instance;
                    if (ctrl != null && ctrl.currFloor != null) tile = ctrl.currFloor.seqID;
                }
                catch { }
                return State.InDanger(tile) ? "1" : "0";
            }
        }

        [Tag(Name = "SJ_TilesText", Desc = "轨道数（危险时变红）")]
        public static string TilesText
        {
            get
            {
                State.DebugTiles++;
                int cur = -1, total = -1;
                try
                {
                    var ctrl = scrController.instance;
                    if (ctrl != null && ctrl.currFloor != null) cur = ctrl.currFloor.seqID;
                    var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                    if (ld != null && ld.angleData != null) total = ld.angleData.Count;
                }
                catch { }
                string body = $"{cur}/{total}";
                bool danger = State.InDanger(cur);
                if (State.DebugTiles % 200 == 0)
                    Debug.Log($"[StrictJudge] tiles tag cur={cur} danger={danger} zones={State.DangerZones.Count}");
                if (danger) return "<color=#ff4444>" + body + "</color>";
                return body;
            }
        }

        [Tag(Name = "SJ_Attempt", Desc = "当前尝试序号")]
        public static string Attempt => State.Attempt.ToString();
    }

    [HarmonyPatch(typeof(scrMisc), "GetHitMarginInSec")]
    public static class Patch_Margin
    {
        public static void Postfix(Difficulty difficulty, double timeDiff, float floorBpm, float conductorPitch, double marginScale, HitMargin __result)
        {
            try
            {
                if (State.Busy) return;
                if (difficulty == Difficulty.Strict) return;
                if (Math.Abs(timeDiff) < 1e-6 || Math.Abs(timeDiff) > 0.6) return;
                if (scnGame.instance == null) return;

                var ld2 = scnGame.instance.levelData;
                if (ld2 != null && ld2.song != State.CurrentLevel)
                {
                    State.CurrentLevel = ld2.song;
                    State.ResetScanForLevel();
                }
                string newKey = State.BuildKey();
                if (newKey != State.CurrentKey)
                {
                    State.CurrentKey = newKey;
                    State.EnsureAttempt();
                }
                try
                {
                    if (ld2 != null && ld2.angleData != null && !string.IsNullOrEmpty(newKey))
                    {
                        State.LevelTotals[newKey] = ld2.angleData.Count;
                        State.SaveTotals();
                    }
                }
                catch { }
                State.ScanLevelCheckpoints();

                int tile = -1;
                try
                {
                    var ctrl = scrController.instance;
                    if (ctrl != null && ctrl.currFloor != null) tile = ctrl.currFloor.seqID;
                }
                catch { }

                State.Record(timeDiff, __result, floorBpm, conductorPitch, (float)marginScale, tile);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ffxCheckpoint), "StartEffect")]
    public static class Patch_Checkpoint
    {
        public static void Postfix()
        {
            try { State.RegisterCheckpoint(GCS.checkpointNum); } catch { }
        }
    }

    [HarmonyPatch(typeof(scrController), "Start_Rewind")]
    public static class Patch_RunRewind
    {
        public static void Postfix()
        {
            try { State.BeginRun(); } catch { }
        }
    }

    [HarmonyPatch(typeof(scrController), "Awake_Rewind")]
    public static class Patch_RunAwakeRewind
    {
        public static void Postfix()
        {
            try { State.BeginRun(); } catch { }
        }
    }

    [HarmonyPatch(typeof(scrController), "Start")]
    public static class Patch_RunStart
    {
        public static void Postfix()
        {
            try { State.BeginRun(); } catch { }
        }
    }

    [HarmonyPatch(typeof(scrPlayer), "Die")]
    public static class Patch_Die
    {
        public static void Postfix()
        {
            try
            {
                int tile = -1;
                var ctrl = scrController.instance;
                if (ctrl != null && ctrl.currFloor != null) tile = ctrl.currFloor.seqID;
                if (tile >= 0) State.RecordMiss(tile);
                State.MarkDeath();
            }
            catch { }
            State.FinalizeAttempt();
        }
    }

    [HarmonyPatch(typeof(scrController), "OnLandOnPortal")]
    public static class Patch_Clear
    {
        public static void Postfix()
        {
            try
            {
                int tile = -1;
                var ctrl = scrController.instance;
                if (ctrl != null && ctrl.currFloor != null) tile = ctrl.currFloor.seqID;
                if (tile > 0 && State.HitCount > 0) State.RecordPass(tile);
            }
            catch { }
            State.FinalizeAttempt();
        }
    }
}
