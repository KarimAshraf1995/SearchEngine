using System;
using System.IO;
using System.Web;
using System.Runtime.Serialization.Formatters.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Spider
{
    class HtmlParser
    {
        /*** Ranking:
           * keywords(25) -> Title (30) -> description (20) -> H1 (10) -> H2 (8) -> H3 (6) -> H4 (5) -> H5 (5) -> H6 (5) -> b or strong (3) -> P or others (1)
           *
           ***/

        private static readonly Dictionary<string, int> Ranker = new Dictionary<string, int>(){
           {"keywords",50 },
           {"title",50},
           {"description",50 },
           {"h1",10},
           {"h2",8},
           {"h3",6},
           {"h4",5},
           {"h5",5},
           {"h6",5},
           {"b",3},
           {"strong",3},
           {"body",1}
        };

        private static readonly double imagesRank = 0.1;

        private static HashSet<string> stopwords;

        private HtmlDocument doc;

        private string SourceLink;

        private string title;

        private string PlainString;

        public HtmlParser(string html, string link)
        {
            SourceLink = link;
            doc = new HtmlDocument();

            doc.LoadHtml(html);

            doc.DocumentNode.Descendants().Where(n => n.Name == "script" || n.Name == "style" || n.Name == "noscript" || n.NodeType == HtmlNodeType.Comment).ToList().ForEach(n => n.Remove());

            var titletag = doc.DocumentNode.SelectSingleNode("//title");

            if (titletag != null)
                title = HttpUtility.HtmlDecode(titletag.InnerHtml);

            SetPlainText();
        }

        public static void IntializeStopWords()
        {
            string FileName = Properties.Settings.Default.PATH + Properties.Settings.Default.StopWordsFile;
            Stream TestFileStream = File.OpenRead(FileName);
            BinaryFormatter deserializer = new BinaryFormatter();
            stopwords = (HashSet<string>)deserializer.Deserialize(TestFileStream);
            TestFileStream.Close();
        }

        private string FixLink(string link)
        {
            link = link.Replace(@"https://", @"http://");
            link = link.TrimEnd('/');

            if (link.StartsWith(@"mailto:") || link.StartsWith(@"tel:"))
            {
                return null;
            }

            if (!link.StartsWith(@"http://"))
            {
                if (link.StartsWith("//"))
                {
                    return "http:" + link;
                }
                else
                {
                    try
                    {
                        string str = new Uri(new Uri(SourceLink), link).AbsoluteUri;
                        return str.Replace(@"https://", @"http://");
                    }
                    catch(UriFormatException)
                    {
                        return null;
                    }
                }
            }
            return link;
        }

        public IEnumerable<String> GetOutGoingLinks()
        {
            var links_nodes = doc.DocumentNode.SelectNodes("//a[@href]");
            string current_link;
            if (links_nodes != null)
            {
                foreach (var node in links_nodes)
                {
                    current_link = FixLink(node.Attributes["href"].Value);

                    if (current_link != null && current_link.StartsWith("http") && !current_link.Contains("#"))
                        yield return current_link;
                }
            }

        }

        public string GetTitle()
        {
            return title;
        }

        public string GetMetaKeywords()
        {
            var Keywords = doc.DocumentNode.SelectSingleNode("//meta[translate(@name, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='keywords']");
            try
            {
                if (Keywords != null)
                    return HttpUtility.HtmlDecode(Keywords.Attributes["content"].Value);
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public string GetMetaDescription()
        {
            var Description = doc.DocumentNode.SelectSingleNode("//meta[translate(@name, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ','abcdefghijklmnopqrstuvwxyz')='description']");
            try
            {
                if (Description != null)
                    return HttpUtility.HtmlDecode(Description.Attributes["content"].Value);
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private void SetPlainText()
        {
            string text = "";
            string description = GetMetaDescription();
            string keywords = GetMetaKeywords();
            if (description != null)
            {
                text += description;
            }
            if (keywords != null)
            {
                text += keywords;
            }
            PlainString = text + HttpUtility.HtmlDecode(string.Join(" ", doc.DocumentNode.Descendants()
                        .Where(n => !n.HasChildNodes && !string.IsNullOrWhiteSpace(n.InnerText))
                        .Select(n => n.InnerText)));
        }

        public string PlainText()
        {
            return PlainString;
        }

        public Dictionary<string, string> ImagesVectors()
        {
            Dictionary<string, string> imagesdictionary = new Dictionary<string, string>();
            var images = doc.DocumentNode.SelectNodes("//img[@src]");
            string link;
            string alt;

            if (images == null)
                return imagesdictionary;

            foreach (HtmlNode img in images)
            {
                link = FixLink(img.Attributes["src"].Value);

                if (link == null)
                    continue;

                alt = null;
                if (img.Attributes["alt"] != null && img.Attributes["alt"].Value != "")
                {
                    alt = img.Attributes["alt"].Value;
                }
                else
                {
                    alt = null;
                }
                if (!imagesdictionary.ContainsKey(link) && alt != null)
                    imagesdictionary.Add(link, alt);
            }
            return imagesdictionary;
        }

        public Dictionary<string, double> KeywordsVectorsFromText()
        {
            double divider = 0;

            Dictionary<string, double> dictionary = new Dictionary<string, double>();

            GetMeta(dictionary);

            string word;

            foreach (var NodeType in Ranker)
            {
                var Nodes = doc.DocumentNode.Descendants(NodeType.Key);

                if (Nodes.Count() == 0)
                    continue;

                foreach (var Node in Nodes)
                {
                    var KeywordsMatches = Regex.Matches(HttpUtility.HtmlDecode(
                        string.Join(" ", Node.Descendants()
                        .Where(n => !n.HasChildNodes && !string.IsNullOrWhiteSpace(n.InnerText))
                        .Select(n => n.InnerText))
                        ), @"\b\w{2,}\b", RegexOptions.Compiled);

                    foreach (Match KeywordMatch in KeywordsMatches)
                    {
                        word = KeywordMatch.Value.ToLowerInvariant();

                        if (!stopwords.Contains(word))
                        {
                            IStemmer p = new Porter2();
                            word = p.stem(word);

                            if (dictionary.ContainsKey(word))
                                dictionary[word] += NodeType.Value;
                            else
                                dictionary.Add(word, NodeType.Value);
                        }
                    }
                }

                Nodes.ToList().ForEach(N => N.Remove());
            }

            divider = dictionary.Values.Max();

            foreach(var Key in dictionary.Keys.ToList())
            {
                dictionary[Key] = Math.Round(dictionary[Key] / divider, 4);
            }

            return dictionary;
        }

        public Dictionary<string, double> KeywordsVectorsFromImages(Dictionary<string, string> images)
        {
            var imageDictionary = new Dictionary<string, double>();
            var clairf = new Clairf();
            foreach (var image in images)
            {
                String[] result = clairf.getImageTags(image.Key);
                if (result != null)
                    foreach(var tag in result)
                    {
                        if (imageDictionary.ContainsKey(tag))
                            imageDictionary[tag] += imagesRank;
                        else
                            imageDictionary.Add(tag, imagesRank);
                    }
            }

            return imageDictionary;
        }

        public Dictionary<string, double> MergeDictionaries(Dictionary<string,double> dict1, Dictionary<string, double> dict2)
        {
            foreach (KeyValuePair<string, double> wordVector in dict2)
            {
                if (dict1.ContainsKey(wordVector.Key))
                    dict1[wordVector.Key] += wordVector.Value;
                else
                    dict1.Add(wordVector.Key, wordVector.Value);
            }

            return dict1;
        }

        private void GetMeta(Dictionary<string, double> dictionary)
        {
            string description = GetMetaDescription();

            if (description != null)
            {
                var arr = description.Split(' ');
                foreach (var w in arr)
                {
                    if (!stopwords.Contains(w) && w!=string.Empty)
                    {
                        IStemmer p = new Porter2();
                        string stemmed = p.stem(w);
                        if (dictionary.ContainsKey(stemmed))
                            dictionary[stemmed] += Ranker["keywords"];
                        else
                            dictionary.Add(stemmed, Ranker["keywords"]);
                    }
                }
            }

            string keywords = GetMetaKeywords();

            if (keywords != null)
            {
                var arr = keywords.Split(',');
                foreach (var w in arr)
                {

                    IStemmer p = new Porter2();
                    string stemmed = p.stem(w);
                    if (w != string.Empty)
                    {
                        if (dictionary.ContainsKey(stemmed))
                            dictionary[stemmed] += Ranker["description"];
                        else
                            dictionary.Add(stemmed, Ranker["description"]);
                    }
                }

            }
        }
    }
}