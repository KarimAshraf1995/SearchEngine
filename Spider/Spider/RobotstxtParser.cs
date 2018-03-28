using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Spider
{
    class RobotstxtParser
    {
        //Cache previous parsing results to save time and avoid downloading the same robots.txt file multiple times.
        //Key is the domain, value is Disallow or Allow rules
        static ConcurrentDictionary<string, MatchCollection> DisallowCache = new ConcurrentDictionary<string, MatchCollection>();
        static ConcurrentDictionary<string, MatchCollection> AllowCache = new ConcurrentDictionary<string, MatchCollection>();

        private RobotstxtParser()
        {

        }
        //Function to check if link if allowed to be crawled 
        public static bool Approved(string link)
        {
            string domain;//Extract domain, if not possible it means that url is not valid. in this case return false to avoid visiting it
            try
            {
                domain = new Uri(link).Host;
            }
            catch (Exception)
            {
                return false;
            }

            MatchCollection Disallows;
            MatchCollection Allows;

            //if we have the rules in cache (the same file is parsed before)
            //no need to download the same file and parse it again
            if (DisallowCache.ContainsKey(domain))
            {
                Allows = AllowCache[domain];
                Disallows = DisallowCache[domain];

                if (Allows == null || Disallows == null)
                    return true;
            }
            else
            {
                string robotstxt = HttpDownloader.GetInstance().GetRobotsTxt(domain);

                // robotstxt is null if there is an error while trying to get it (Example 404 not found) 
                // in this case assume that all links are allowed
                if (robotstxt == null)
                {
                    DisallowCache.TryAdd(domain, null);
                    AllowCache.TryAdd(domain, null);
                    return true;
                }
                //parse file

                //remove comments
                robotstxt = Regex.Replace(robotstxt, @"#.*?$", "", RegexOptions.Multiline);

                //match User-agent: *
                string robotsmatch = Regex.Match(robotstxt, @"(?<=User\-agent\: \*).+?(?=(User-agent\:)|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled).Value;

                //match allows and disallows
                Disallows = Regex.Matches(robotsmatch, @"(?<=Disallow: ).+?(?=(\u000D|( +#)|$))", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
                Allows = Regex.Matches(robotsmatch, @"(?<=[^(Dis)]allow: ).+?(?=(\u000D|( +#)|$))", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

                DisallowCache.TryAdd(domain, Disallows);
                AllowCache.TryAdd(domain, Allows);
            }

            bool allowed;

            //In this case only allowed links are those under Allow rules, others are disallowed
            if (Disallows.Count == 0 && Allows.Count != 0)
                allowed = false;
            //In this case only disallowed links are those under Disallow rules, others are allowed
            else if (Disallows.Count != 0 && Allows.Count == 0)
                allowed = true;
            else
                allowed = true;

            StringBuilder PatternBuilder;
            string pattern;
            //In case of contradicting rules, use the most specific one
            int max_specific_rule = 0;

            foreach (Match disallow in Disallows)
            {
                PatternBuilder = new StringBuilder(disallow.Value).Replace(@"\", @"\\").Replace(@"/", @"\/").Replace(".", @"\.").Replace("?", @"\?").Replace("+", @"\+").Replace("*", @".*?").Replace("(", @"\(").Replace(")", @"\)").Replace("^", @"\^").Replace("$", @"\$").Replace("[", @"\[").Replace("]", @"]").Replace("{", @"\{").Replace("}", @"\}").Replace("|", @"\|");
                pattern = PatternBuilder.ToString();
                if (Regex.IsMatch(link, pattern))
                {
                    max_specific_rule = Math.Max(max_specific_rule, disallow.Value.Length);
                    allowed = false;
                }
            }

            foreach (Match allow in Allows)
            {
                PatternBuilder = new StringBuilder(allow.Value).Replace(@"\", @"\\").Replace(@"/", @"\/").Replace(".", @"\.").Replace("?", @"\?").Replace("+", @"\+").Replace("*", @".*?").Replace("(", @"\(").Replace(")", @"\)").Replace("^", @"\^").Replace("$", @"\$").Replace("[", @"\[").Replace("]", @"]").Replace("{", @"\{").Replace("}", @"\}").Replace("|", @"\|");
                pattern = PatternBuilder.ToString();
                if (Regex.IsMatch(link, pattern) && max_specific_rule < allow.Value.Length)
                {
                    max_specific_rule = allow.Value.Length;
                    allowed = true;
                }
            }

            return allowed;
        }
    }
}
