using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulp
{
    static class Links
    {
        static readonly Regex SingleUrl = new Regex(@"^\s*https?://[^\s""<>]+\s*$", RegexOptions.IgnoreCase);

        public static bool IsSingleUrl(string text)
            => text != null && text.Length < 8192 && SingleUrl.IsMatch(text);

        // "https://www.youtube.com/watch?v=x" -> "youtube.com/watch?v=x"
        public static string Display(string url)
        {
            string u = url.Trim();
            int i = u.IndexOf("://", StringComparison.Ordinal);
            if (i > 0) u = u.Substring(i + 3);
            if (u.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) u = u.Substring(4);
            return u.TrimEnd('/');
        }

        public static string Host(string url)
        {
            try
            {
                var h = new Uri(url.Trim()).Host;
                return h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h.Substring(4) : h;
            }
            catch { return ""; }
        }
    }

    // Removes tracking from links using plain-text rules. Only known parameters are touched, the rest of the
    // link is kept byte for byte, and anything that doesn't parse cleanly is left alone.
    class LinkCleaner
    {
        public static string UserRulesPath => Path.Combine(Settings.Dir, "link-rules.txt");

        public const string BuiltIn = @"# Built-in rules (read-only). Your own rules in link-rules.txt are added on top.
# Format:   site : param, param, prefix_*        remove these query parameters
#           keep site : param                    never remove these (overrides built-ins)
#           unwrap site/path : param             the real link is inside this parameter
#           shorten site                         Amazon /dp/ID and noon /SKU/p/ product links
#           skip site                            never touch this site
# A site also covers its subdomains. ""amazon.*"" means any Amazon country. ""*"" means every site.

# tracking used across the web
* : utm_*, fbclid, gclid, gclsrc, dclid, gbraid, wbraid, gad_source, gad_campaignid, srsltid, msclkid, yclid, twclid, ttclid, li_fat_id, igshid, mc_cid, mc_eid, _hsenc, _hsmi, __hssc, __hstc, __hsfp, hsctatracking, mkt_tok, oly_anon_id, oly_enc_id, vero_id, vero_conv, _openstat, rb_clickid, wickedid, epik, _ga, _gl

# video, music, social
youtube.com : si, feature, pp, ab_channel, source_ve_path, embeds_referring_euri, embeds_referring_origin, embeds_euri
youtu.be : si, feature
spotify.com : si
instagram.com : igsh, igshid
threads.net : igsh, xmt
threads.com : igsh, xmt
tiktok.com : _r, _t, is_from_webapp, sender_device, sender_web_id, is_copy_url, share_app_id, share_item_id, share_link_id, social_sharing, tt_from, u_code, user_id, sec_user_id, timestamp, refer, web_id, enter_from, enter_method, previous_page
x.com : s, t, ref_src, ref_url
twitter.com : s, t, ref_src, ref_url
facebook.com : mibextid, rdid, sfnsn, __tn__, __cft__*, __xts__*, ref, fref, hc_ref, hc_location
fb.watch : mibextid
reddit.com : share_id, rdt, ref_source, ref_campaign, correlation_id
linkedin.com : trackingid, trk, trkinfo, lipi, refid, rcm, midtoken, midsig, eid, trkemail, originalsubdomain

# shopping
amazon.* : ref, ref_, pf_rd_*, pd_rd_*, qid, sr, crid, sprefix, content-id, _encoding, dib, dib_tag, linkcode, linkid, tag, ascsubtag
shorten amazon.*
shorten noon.com
aliexpress.com : spm, scm, scm_id, scm-url, algo_pvid, algo_exp_id, pdp_npi, pdp_ext_f, pvid, utparam-url, aff_fcid, aff_fsk, aff_platform, aff_trace_key, terminal_id, sk, afsmartredirect
alibaba.com : spm, scm
taobao.com : spm, scm
tmall.com : spm, scm

# search pages
google.*/search : ei, ved, uact, oq, gs_lcrp, gs_lp, sclient, sca_esv, sca_upv, sxsrf, iflsig, rlz, sourceid, aqs, bih, biw, dpr, ie
bing.com/search : form, cvid, sp, qs, pq, sc, sk, ghc, lq

# redirect wrappers
unwrap google.*/url : q, url
unwrap l.facebook.com/l.php : u
unwrap lm.facebook.com/l.php : u
unwrap l.messenger.com/l.php : u
unwrap l.instagram.com : u
unwrap l.threads.net : u
unwrap youtube.com/redirect : q
unwrap out.reddit.com : url
unwrap *.safelinks.protection.outlook.com : url
unwrap steamcommunity.com/linkfilter : url, u
unwrap duckduckgo.com/l : uddg
unwrap slack-redir.net/link : url
unwrap linkedin.com/redir/redirect : url
unwrap vk.com/away.php : to
unwrap t.umblr.com/redirect : z
";

        public const string UserTemplate = @"# Your own link-cleaner rules, added on top of the built-in ones.
# One rule per line. Lines starting with # are ignored. Examples:
#
# example.com : ref, share_id   remove these (subdomains too)
# * : some_tracker              remove a param on every site
# keep youtube.com : pp         never remove it (beats built-ins)
# unwrap out.example.com : url  redirect link, real link in ?url=
# skip mybank.com               never touch links to this site
# shop.com : track_*            a * at the end matches a prefix
";

        class Rule
        {
            public Regex Host;      // null = every site
            public string Path;     // path prefix or null
            public string[] Params; // lower-case; "x*" = prefix
            public bool Matches(string host, string path) =>
                (Host == null || Host.IsMatch(host)) && (Path == null || path.StartsWith(Path, StringComparison.OrdinalIgnoreCase));
        }

        readonly List<Rule> remove = new List<Rule>(), keep = new List<Rule>(), unwrap = new List<Rule>(), shorten = new List<Rule>(), skip = new List<Rule>();
        public readonly List<string> Errors = new List<string>();

        public LinkCleaner(string userRules)
        {
            Parse(BuiltIn, "built-in");
            Parse(userRules ?? "", "link-rules.txt");
        }

        public static LinkCleaner LoadDefault()
        {
            string user = "";
            try
            {
                if (!File.Exists(UserRulesPath)) { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(UserRulesPath, UserTemplate, Encoding.UTF8); }
                user = File.ReadAllText(UserRulesPath, Encoding.UTF8);
            }
            catch { }
            return new LinkCleaner(user);
        }

        static Regex HostRegex(string pat)
        {
            pat = pat.Trim().ToLowerInvariant();
            if (pat == "*" || pat.Length == 0) return null;
            string body = Regex.Escape(pat).Replace(@"\*", @"[a-z0-9-]+(?:\.[a-z0-9-]+)?");
            return new Regex(@"^(?:[a-z0-9-]+\.)*" + body + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        static Rule MakeRule(string site, string paramList)
        {
            site = site.Trim();
            int slash = site.IndexOf('/');
            string host = slash >= 0 ? site.Substring(0, slash) : site;
            string path = slash >= 0 ? site.Substring(slash) : null;
            return new Rule
            {
                Host = HostRegex(host),
                Path = path,
                Params = (paramList ?? "").Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToArray(),
            };
        }

        void Parse(string text, string source)
        {
            int n = 0;
            foreach (var raw in text.Split('\n'))
            {
                n++;
                string line = raw.Trim();
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line.Substring(0, hash).Trim();
                if (line.Length == 0) continue;
                try
                {
                    string verb = null;
                    foreach (var v in new[] { "unwrap ", "keep ", "shorten ", "skip " })
                        if (line.StartsWith(v, StringComparison.OrdinalIgnoreCase)) { verb = v.Trim().ToLowerInvariant(); line = line.Substring(v.Length).Trim(); break; }
                    int colon = line.IndexOf(':');
                    string site = colon >= 0 ? line.Substring(0, colon) : line;
                    string list = colon >= 0 ? line.Substring(colon + 1) : "";
                    if (site.Trim().Length == 0 || site.Trim().Contains(" ")) throw new FormatException("bad site");
                    var r = MakeRule(site, list);
                    switch (verb)
                    {
                        case "unwrap": if (r.Params.Length == 0) throw new FormatException("unwrap needs a parameter"); unwrap.Add(r); break;
                        case "keep": keep.Add(r); break;
                        case "shorten": shorten.Add(r); break;
                        case "skip": skip.Add(r); break;
                        default: if (r.Params.Length == 0) throw new FormatException("no parameters listed"); remove.Add(r); break;
                    }
                }
                catch (Exception ex) { Errors.Add(source + " line " + n + ": " + raw.Trim() + " (" + ex.Message + ")"); }
            }
        }

        static bool ParamMatch(string name, string[] pats)
        {
            foreach (var p in pats)
            {
                if (p.EndsWith("*")) { if (name.StartsWith(p.Substring(0, p.Length - 1), StringComparison.Ordinal)) return true; }
                else if (name == p) return true;
            }
            return false;
        }

        static readonly Regex AmazonHost = new Regex(@"(^|\.)amazon\.[a-z.]{2,7}$", RegexOptions.IgnoreCase);
        static readonly Regex AmazonAsin = new Regex(@"/(?:dp|gp/product|gp/aw/d|exec/obidos/asin|o|gp/offer-listing)/([A-Z0-9]{10})(?=[/?#]|$)", RegexOptions.IgnoreCase);
        static readonly Regex NoonHost = new Regex(@"(^|\.)noon\.com$", RegexOptions.IgnoreCase);
        static readonly Regex NoonProduct = new Regex(@"^/(?<loc>[a-z]{2,8}-[a-z]{2})/(?:[^/]+/)?(?<sku>[A-Z0-9]{8,30})/p/?$");

        // returns the cleaned link, or null if nothing should change; `what` lists the changes
        public string Clean(string input, List<string> what = null)
        {
            if (input == null) return null;
            string url = input.Trim();
            if (url.Length == 0 || url.Length > 8000 || url.Any(char.IsWhiteSpace)) return null;
            string result = url;
            for (int depth = 0; depth < 4; depth++)
            {
                string next = CleanOnce(result, what, out bool unwrapped);
                if (next == null) break;
                result = next;
                if (!unwrapped) break;   // after unwrapping, clean the inner link too
            }
            return result == url ? null : result;
        }

        string CleanOnce(string url, List<string> what, out bool unwrapped)
        {
            unwrapped = false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return null;
            string host = u.Host.ToLowerInvariant(), path = u.AbsolutePath;
            if (skip.Any(r => r.Matches(host, path))) return null;

            int hashAt = url.IndexOf('#');
            string frag = hashAt >= 0 ? url.Substring(hashAt) : "";
            string rest = hashAt >= 0 ? url.Substring(0, hashAt) : url;
            int qAt = rest.IndexOf('?');
            string basePart = qAt >= 0 ? rest.Substring(0, qAt) : rest;
            string query = qAt >= 0 ? rest.Substring(qAt + 1) : null;
            var parts = query == null ? new List<string>() : query.Split('&').ToList();

            Func<string, string> nameOf = p =>
            {
                int eq = p.IndexOf('=');
                string n = eq >= 0 ? p.Substring(0, eq) : p;
                try { n = Uri.UnescapeDataString(n); } catch { }
                return n.ToLowerInvariant();
            };
            Func<string, string> valueOf = p =>
            {
                int eq = p.IndexOf('=');
                if (eq < 0) return "";
                try { return Uri.UnescapeDataString(p.Substring(eq + 1)); } catch { return null; }
            };

            // 1. redirect wrappers: hand back the real link
            foreach (var r in unwrap)
            {
                if (!r.Matches(host, path)) continue;
                foreach (var pn in r.Params)
                {
                    var hit = parts.FirstOrDefault(p => nameOf(p) == pn);
                    if (hit == null) continue;
                    string inner = valueOf(hit);
                    if (inner != null && !inner.Any(char.IsWhiteSpace) && Uri.TryCreate(inner, UriKind.Absolute, out Uri iu) &&
                        (iu.Scheme == Uri.UriSchemeHttp || iu.Scheme == Uri.UriSchemeHttps) && iu.Host.Length > 0)
                    {
                        what?.Add("unwrapped a " + Links.Host(url) + " redirect");
                        unwrapped = true;
                        return inner;
                    }
                }
            }

            // 2. product links -> their short canonical form
            if (shorten.Any(r => r.Matches(host, path)))
            {
                if (AmazonHost.IsMatch(host))
                {
                    var m = AmazonAsin.Match(path);
                    if (m.Success)
                    {
                        string canon = u.Scheme + "://" + u.Authority + "/dp/" + m.Groups[1].Value.ToUpperInvariant();
                        if (canon != url) { what?.Add("shortened the Amazon product link"); return canon; }
                        return null;
                    }
                    string trimmed = Regex.Replace(basePart, @"(?<=[^/:])/ref=[^/?#]*", "");
                    if (trimmed != basePart) { what?.Add("ref"); basePart = trimmed; }
                }
                else if (NoonHost.IsMatch(host))
                {
                    var m = NoonProduct.Match(path);
                    if (m.Success)
                    {
                        string canon = u.Scheme + "://" + u.Authority + "/" + m.Groups["loc"].Value + "/" + m.Groups["sku"].Value + "/p/";
                        if (canon != url) { what?.Add("shortened the noon product link"); return canon; }
                        return null;
                    }
                }
            }

            // 3. tracking parameters
            var keepRules = keep.Where(r => r.Matches(host, path)).ToList();
            var removeRules = remove.Where(r => r.Matches(host, path)).ToList();
            var kept = new List<string>();
            bool changed = basePart != (qAt >= 0 ? rest.Substring(0, qAt) : rest);
            bool removed = false;
            foreach (var p in parts)
            {
                if (p.Length == 0) continue;
                string n = nameOf(p);
                bool drop = removeRules.Any(r => ParamMatch(n, r.Params)) && !keepRules.Any(r => ParamMatch(n, r.Params));
                if (drop) { removed = true; what?.Add(n); }
                else kept.Add(p);
            }
            if (!changed && !removed) return null;   // only ever rewrite when something known was taken out
            if (!removed) return basePart + (query != null ? "?" + query : "") + frag;
            return basePart + (kept.Count > 0 ? "?" + string.Join("&", kept) : "") + frag;
        }

        public string Describe(string input)
        {
            var what = new List<string>();
            string r = Clean(input, what);
            if (r == null) return null;
            var named = what.Where(w => !w.Contains(" ")).Distinct().ToList();
            var sentences = what.Where(w => w.Contains(" ")).Distinct().ToList();
            if (named.Count > 0) sentences.Add("removed " + string.Join(", ", named));
            return string.Join("; ", sentences);
        }
    }
}
