using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
namespace HiveBabble.Translate;
/// <summary>First-run download of Argos language models into the bundled translator's models folder (installer build only). Works offline once models are present.</summary>
public static class ModelManager
{
    const string IndexUrl = "https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json";
    // Fallback links (Argos package index, Oct 2026) if the index can't be read.
    static readonly Dictionary<string, string> Fallback = new()
    {
        ["ja_en"] = "https://argos-net.com/v1/translate-ja_en-1_1.argosmodel",
        ["zh_en"] = "https://argos-net.com/v1/translate-zh_en-1_9.argosmodel",
        ["ko_en"] = "https://argos-net.com/v1/translate-ko_en-1_1.argosmodel",
        ["es_en"] = "https://argos-net.com/v1/translate-es_en-1_9.argosmodel",
        ["fr_en"] = "https://argos-net.com/v1/translate-fr_en-1_9.argosmodel",
        ["de_en"] = "https://argos-net.com/v1/translate-de_en-1_3.argosmodel",
        ["ru_en"] = "https://argos-net.com/v1/translate-ru_en-1_9.argosmodel",
        ["en_ja"] = "https://argos-net.com/v1/translate-en_ja-1_1.argosmodel",
        ["en_es"] = "https://argos-net.com/v1/translate-en_es-1_0.argosmodel",
    };
    static readonly Dictionary<string, string> Names = new()
    { ["en"] = "English", ["ja"] = "Japanese", ["zh"] = "Chinese", ["ko"] = "Korean", ["es"] = "Spanish", ["fr"] = "French", ["de"] = "German", ["ru"] = "Russian" };
    static readonly string[] Sources = { "ja", "zh", "ko", "es", "fr", "de", "ru" };
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    public static string ModelsDir => Path.Combine(TranslatorHost.BundledDir, "models");

    static string Norm(string c) { c = (c ?? "").Trim().ToLowerInvariant(); return c.StartsWith("zh") ? "zh" : c.Split('-')[0]; }

    /// <summary>Language pairs needed for the current settings: every source into English, plus English into the target when it isn't English.</summary>
    public static List<string> Required(string source, string target)
    {
        string s = Norm(source), t = Norm(target);
        var pairs = new List<string>();
        if (s == "auto" || s == "") pairs.AddRange(Sources.Select(x => x + "_en"));
        else if (s != "en") pairs.Add(s + "_en");
        if (t != "en" && t != "") pairs.Add("en_" + t);
        return pairs.Where(Fallback.ContainsKey).Distinct().ToList();
    }

    public static HashSet<string> Installed()
    {
        var set = new HashSet<string>();
        if (!Directory.Exists(ModelsDir)) return set;
        foreach (var meta in Directory.GetDirectories(ModelsDir).Select(d => Path.Combine(d, "metadata.json")).Where(File.Exists))
        {
            try { using var j = JsonDocument.Parse(File.ReadAllText(meta)); set.Add(Norm(j.RootElement.GetProperty("from_code").GetString()!) + "_" + Norm(j.RootElement.GetProperty("to_code").GetString()!)); } catch { }
        }
        return set;
    }

    static async Task<Dictionary<string, string>> LinksAsync()
    {
        var links = new Dictionary<string, string>(Fallback);
        try
        {
            using var j = JsonDocument.Parse(await Http.GetStringAsync(IndexUrl));
            foreach (var p in j.RootElement.EnumerateArray())
            {
                string key = Norm(p.GetProperty("from_code").GetString()!) + "_" + Norm(p.GetProperty("to_code").GetString()!);
                if (links.ContainsKey(key) && p.GetProperty("links").GetArrayLength() > 0) links[key] = p.GetProperty("links")[0].GetString()!;
            }
        }
        catch (Exception ex) { Log.Write("Model index unavailable, using built-in links: " + ex.GetType().Name); }
        return links;
    }

    /// <summary>Downloads missing models. Returns false only when no model at all is available.</summary>
    public static async Task<bool> EnsureModelsAsync(string source, string target, Action<string> status, Func<bool> cancelled)
    {
        if (!Directory.Exists(TranslatorHost.BundledDir)) return true; // source build: models managed by Start-Translator.ps1
        Directory.CreateDirectory(ModelsDir);
        var have = Installed();
        var missing = Required(source, target).Where(p => !have.Contains(p)).ToList();
        if (missing.Count == 0) return true;
        Log.Write($"Models missing: {string.Join(",", missing)}; downloading.");
        var links = await LinksAsync();
        int done = 0, failed = 0;
        foreach (var pair in missing)
        {
            if (cancelled()) return have.Count > 0;
            string label = string.Join(" \u2192 ", pair.Split('_').Select(c => Names.GetValueOrDefault(c, c)));
            bool ok = false;
            for (int attempt = 1; attempt <= 3 && !ok && !cancelled(); attempt++)
            {
                try { await DownloadOneAsync(links[pair], p => status($"Downloading language model {done + 1}/{missing.Count}: {label} {p}%"), cancelled); ok = true; }
                catch (Exception ex)
                {
                    Log.Write($"Model {pair} download attempt {attempt} failed: {ex.GetType().Name}");
                    if (attempt < 3) { status($"Download failed for {label}, retrying ({attempt}/3)\u2026"); await Task.Delay(attempt * 4000); }
                }
            }
            if (ok) { done++; have.Add(pair); Log.Write($"Model {pair} installed."); } else failed++;
        }
        if (failed > 0)
            status(have.Count > 0 ? $"{failed} language model(s) couldn't download. Hive-Babble will try again next launch." : "Couldn't download language models. Check your internet connection and restart Hive-Babble.");
        return have.Count > 0;
    }

    static async Task DownloadOneAsync(string url, Action<int> progress, Func<bool> cancelled)
    {
        string part = Path.Combine(ModelsDir, ".download.part"), tmp = Path.Combine(ModelsDir, ".extract");
        try
        {
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? -1, read = 0; int last = -1;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(part);
                var buf = new byte[1 << 16]; int n; var tick = Stopwatch.StartNew();
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    await dst.WriteAsync(buf.AsMemory(0, n)); read += n;
                    int pct = total > 0 ? (int)(read * 100 / total) : 0;
                    if (pct != last && tick.ElapsedMilliseconds > 250) { last = pct; tick.Restart(); progress(pct); }
                }
                if (total > 0 && read != total) throw new IOException("Incomplete download.");
            }
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            await Task.Run(() => ZipFile.ExtractToDirectory(part, tmp));
            var dir = Directory.GetDirectories(tmp).FirstOrDefault(d => File.Exists(Path.Combine(d, "metadata.json"))) ?? throw new InvalidDataException("Not a language model package.");
            string final = Path.Combine(ModelsDir, Path.GetFileName(dir));
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.Move(dir, final);
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
        }
    }
}
