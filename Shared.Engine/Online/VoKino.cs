﻿using Shared.Model.Online.VoKino;
using Shared.Model.Templates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Shared.Engine.Online
{
    public class VoKinoInvoke
    {
        #region VoKinoInvoke
        string? host;
        string apihost;
        string token;
        Func<string, ValueTask<string?>> onget;
        Func<string, string> onstreamfile;
        Func<string, string>? onlog;
        Action? requesterror;

        public VoKinoInvoke(string? host, string apihost, string token, Func<string, ValueTask<string?>> onget, Func<string, string> onstreamfile, Func<string, string>? onlog = null, Action? requesterror = null)
        {
            this.host = host != null ? $"{host}/" : null;
            this.apihost = apihost;
            this.token = token;
            this.onget = onget;
            this.onstreamfile = onstreamfile;
            this.onlog = onlog;
            this.requesterror = requesterror;
        }
        #endregion

        #region Embed
        public async ValueTask<EmbedModel?> Embed(long kinopoisk_id, string? balancer)
        {
            try
            {
                if (string.IsNullOrEmpty(balancer))
                {
                    string? json = await onget($"{apihost}/v2/view/{kinopoisk_id}?token={token}");
                    if (json == null)
                    {
                        requesterror?.Invoke();
                        return null;
                    }

                    if (json.StartsWith("Фильм не найден"))
                        return new EmbedModel() { IsEmpty = true };

                    var similars = new List<Similar>() {  Capacity = 10 };

                    foreach (var item in JsonSerializer.Deserialize<JsonElement>(json).GetProperty("online").EnumerateObject())
                    {
                        string? playlistUrl = item.Value.GetProperty("playlist_url").GetString();
                        if (string.IsNullOrEmpty(playlistUrl))
                            continue;

                        var model = new Similar()
                        {
                            title = item.Name,
                            balancer = Regex.Match(playlistUrl, "/v2/online/([^/]+)/").Groups[1].Value
                        };

                        if (item.Name == "Vokino")
                            similars.Insert(0, model);
                        else
                            similars.Add(model);
                    }

                    return new EmbedModel() { similars = similars };
                }
                else
                {
                    string? json = await onget($"{apihost}/v2/online/{balancer}/{kinopoisk_id}?token={token}");
                    if (json == null)
                    {
                        requesterror?.Invoke();
                        return null;
                    }

                    if (json.StartsWith("{\"error\":"))
                        return new EmbedModel() { IsEmpty = true };

                    var root = JsonSerializer.Deserialize<RootObject>(json);
                    if (root?.channels == null || root.channels.Count == 0)
                        return null;

                    return new EmbedModel() { channels = root.channels };
                }
            }
            catch { }

            return null;
        }
        #endregion

        #region Html
        public string Html(EmbedModel? result, long kinopoisk_id, string? title, string? original_title, string? balancer, int s)
        {
            if (result == null || result.IsEmpty)
                return string.Empty;

            string? enc_title = HttpUtility.UrlEncode(title);
            string? enc_original_title = HttpUtility.UrlEncode(original_title);

            #region similar
            if (result.similars != null)
            {
                var stpl = new SimilarTpl(result.similars.Count);

                foreach (var similar in result.similars)
                {
                    string link = host + $"lite/vokino?kinopoisk_id={kinopoisk_id}&title={enc_title}&original_title={enc_original_title}&balancer={similar.balancer}";

                    stpl.Append(similar.title, string.Empty, string.Empty, link);
                }

                return stpl.ToHtml();
            }
            #endregion

            if (result?.channels == null || result.channels.Count == 0)
                return string.Empty;

            if (result.channels.First().playlist_url == "submenu")
            {
                if (s == -1)
                {
                    var tpl = new SeasonTpl(quality: result.channels[0].quality_full?.Replace("2160p.", "4K "));

                    foreach (var ch in result.channels)
                    {
                        string sname = Regex.Match(ch.title, "^([0-9]+)").Groups[1].Value;
                        if (string.IsNullOrEmpty(sname))
                            sname = Regex.Match(ch.title, "([0-9]+)$").Groups[1].Value;

                        tpl.Append(ch.title, host + $"lite/vokino?kinopoisk_id={kinopoisk_id}&balancer={balancer}&title={enc_title}&original_title={enc_original_title}&s={sname}");
                    }

                    return tpl.ToHtml();
                }
                else
                {
                    var tpl = new EpisodeTpl();

                    foreach (var e in result.channels.First(i => i.title.StartsWith($"{s} ") || i.title.EndsWith($" {s}")).submenu)
                    {
                        string ename = Regex.Match(e.ident, "([0-9]+)$").Groups[1].Value;
                        tpl.Append(e.title, $"{title ?? original_title} ({e.title})", s.ToString(), ename, onstreamfile(e.stream_url));
                    }

                    return tpl.ToHtml();
                }
            }
            else
            {
                var mtpl = new MovieTpl(title, original_title, result.channels.Count);

                foreach (var ch in result.channels)
                {
                    string name = ch.quality_full;
                    if (!string.IsNullOrWhiteSpace(name.Replace("2160p.", "")))
                    {
                        name = name.Replace("2160p.", "4K ");

                        if (ch.extra != null && ch.extra.TryGetValue("size", out string? size) && !string.IsNullOrEmpty(size))
                            name += $" - {size}";
                    }

                    mtpl.Append(name, onstreamfile(ch.stream_url));
                }

                return mtpl.ToHtml();
            }
        }
        #endregion
    }
}
