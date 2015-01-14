using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using GilyonosC.GilyonosDSTableAdapters;

namespace GilyonosC
{
    internal static class Downloadfiles
    {
        private static string _encode;
        private static string BaseFolder;
        private static string Server;
        private static string _foldername;
        private static string _specialfolder1;
        private static string _specialfolder2;
        private static readonly GilyonosDS ds = new GilyonosDS();
        private static readonly GilyonosTableAdapter Da = new GilyonosDSTableAdapters.GilyonosTableAdapter();
        private static readonly GilyonListTableAdapter DaList = new GilyonosDSTableAdapters.GilyonListTableAdapter();
        private static readonly QueriesTableAdapter updateQueriesTableAdapter = new QueriesTableAdapter();

        public static void Main()
        {
            Console.WriteLine("Enter the Parsha (Folder Name)");
            _foldername = Console.ReadLine();

            if (string.IsNullOrEmpty(_foldername)) return;

            GilyonosDSTableAdapters.SitesTableAdapter Sitesist = new GilyonosDSTableAdapters.SitesTableAdapter();
            Sitesist.Fill(ds.Sites);

            var qry = from site in ds.Sites
                select site;

            foreach (var p_site in qry)
            {
                BaseFolder = p_site.LocalFolder;

                Server = p_site.BaseURL;
                SetFolder(BaseFolder + @"\" + _foldername);

                _specialfolder1 = _foldername + p_site.SpecialFolder1;
                SetFolder(BaseFolder + @"\" + _specialfolder1);

                _specialfolder2 = _foldername + p_site.SpecialFolder2;
                SetFolder(BaseFolder + @"\" + _specialfolder2);

                _encode = p_site.Encoding;

                CheckForLinks(p_site.DLPage, p_site.RegEx);
            }
            
                

        }

        public static void CheckForLinks(string url, string pattern)
        {
            string str;
            string filename;
            string redirstr;
            bool isYiddish = false;
            string foldname;

            // the the html for the given url
            str = GetData(url, _encode);

            // fill up the dataset
            Da.Fill(ds.Gilyonos);
            DaList.Fill(ds.GilyonList);

            Regex mFoundRegex = new Regex("href\\s*=\\s*[\"']?(?<Link>showgil.aspx.*?)\".*?<div.*?<span[^>]+>(?<Desc>.*?)</span>.*?</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

            // match the pattren against the HTML
            MatchCollection found = mFoundRegex.Matches(str);

            //write the individual news items
            foreach (Match aMatch in found)
            {
                string link = Server + "/" + aMatch.Groups["Link"].Value;
                link = HttpUtility.HtmlDecode(link);

                string pLang = "";
                string pChasidus = "";

                string gid = Regex.Replace(link, "^.*?gil=(\\d*)", "$1");
                int igid = Convert.ToInt32(gid);

                MatchCollection langMatch =
                    new Regex(".*?lblGilLang.*?>(?<Lang>.*?)</span>",
                        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace).Matches(aMatch.Value);

                if (langMatch.Count > 0)
                {
                    pLang = langMatch[0].Groups["Lang"].Value;
                }

                MatchCollection chasidMatch =
                    new Regex(".*?lblGilHug.*?>(?<Chasidus>.*?)</span>",
                        RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace).Matches(aMatch.Value);

                if (chasidMatch.Count > 0)
                {
                    pChasidus = chasidMatch[0].Groups["Chasidus"].Value;
                }

                // setup the Gilyon Header for processing
                GilyonosDS.GilyonListRow gilHead = ds.GilyonList.NewGilyonListRow();
                gilHead.Title = aMatch.Groups["Desc"].Value;
                gilHead.Chasidus = pChasidus;
                gilHead.Lang = pLang;
                gilHead.GilyonID = igid;
                gilHead.Exclude = false;
                gilHead.UseSpecialFolder = false;
                gilHead.Yiddish = false;
                gilHead.Special1 = false;
                gilHead.Special2 = false;

                // check if gilyon header exists, if not create it
                CheckGilyonHeader(ref gilHead);

                // use either default folder or special folder
                if (!gilHead.IsSpecial1Null() && gilHead.Special1)
                    foldname = _specialfolder1;
                else if (!gilHead.IsSpecial2Null() && gilHead.Special2)
                    foldname = _specialfolder2;
                else
                {
                    foldname = _foldername;
                }

                var query = from gilyon in ds.Gilyonos
                            where gilyon.URL == link && gilyon.Parsha == foldname
                            select new { gilyon.URL };

                if (query.Any())
                {
                    continue;
                }


                if (gilHead.Exclude) continue;

                try
                {
                    redirstr = GetData(link, _encode);

                    Regex myRegex = new Regex("<iframe.*?ifrmGilayon.*?src=\"(?<URL>.*?)\">", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

                    //' Capture the first Match, if any, in the InputText
                    string pdflink = myRegex.Match(redirstr).Groups["URL"].Value;
                    //'pdflink = MyRegex.Groups("URL").Value

                    string[] fullurl = null;
                    fullurl = pdflink.Split('/');
                    filename = fullurl[fullurl.GetUpperBound(0)];

                    filename = filename.Replace("\"", "_");
                    filename = filename.Replace("'", "_");
                    filename = filename.Replace("&#39;", "_");

                    pdflink = pdflink.Replace("&#39;", "'");

                    WebClient client = new WebClient();
                    client.DownloadFile(Server + "/" + HttpUtility.UrlPathEncode(pdflink), BaseFolder + @"\" + foldname + @"\" + filename);

                    GilyonosDS.GilyonosRow dr = ds.Gilyonos.NewGilyonosRow();
                    dr.Filename = filename;
                    dr.Parsha = foldname;
                    dr.Title = aMatch.Groups["Desc"].Value;
                    dr.URL = link;
                    ds.Gilyonos.Rows.Add(dr);

                    Da.Update(dr);

                    Console.WriteLine("Downloaded: " + link);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        private static GilyonosDS.GilyonListRow CheckGilyonHeader(ref GilyonosDS.GilyonListRow gilHead)
        {
            // check if we already to header info for this Gilyon
            int gid = gilHead.GilyonID;
            var queryList = (from listitem in ds.GilyonList
                             where listitem.GilyonID == gid
                select new
                {
                    listitem.ID,
                    listitem.Title,
                    listitem.Exclude,
                    listitem.Yiddish,
                    listitem.Chasidus,
                    listitem.Lang,
                    listitem.GilyonID,
                    listitem.Special1,
                    listitem.Special2,
                    listitem.UseSpecialFolder
                }).SingleOrDefault();


            // if not then insert new record
            if (queryList == null)
            {
                // add record to list
                ds.GilyonList.Rows.Add(gilHead);

                DaList.Update(gilHead);

                updateQueriesTableAdapter.RuleSet1();
                updateQueriesTableAdapter.RuleSet2();

                // get the record again from the database, as some rules may have been applied on the db side
                queryList = (from listitem in ds.GilyonList
                            where listitem.GilyonID == gid
                            select new
                            {
                                listitem.ID,
                                listitem.Title,
                                listitem.Exclude,
                                listitem.Yiddish,
                                listitem.Chasidus,
                                listitem.Lang,
                                listitem.GilyonID,
                                listitem.Special1,
                                listitem.Special2,
                                listitem.UseSpecialFolder
                            }).SingleOrDefault();
            }

            gilHead.GilyonID = queryList.GilyonID;
            gilHead.Chasidus = queryList.Chasidus;
            gilHead.Exclude = queryList.Exclude;
            gilHead.Lang = queryList.Lang;
            gilHead.Special1 = queryList.Special1;
            gilHead.Special2 = queryList.Special2;
            gilHead.Title = queryList.Title;
            gilHead.UseSpecialFolder = queryList.UseSpecialFolder;
            gilHead.Yiddish = queryList.Yiddish;


            return gilHead;
        }

        private static bool SetFolder(string foldname)
        {
            if (Directory.Exists(foldname))
            {
                return true;
            }

            Directory.CreateDirectory(foldname);

            return false;
        }

        private static string GetData(string url, string encode = "")
        {
            try
            {
                WebClient req = new WebClient();

                req.Headers.Add("Accept", "*/*");
                req.Headers.Add("UA-CPU", "x86");
                req.Headers.Add("User-Agent", "Mozilla/4.0 (compatible; MSIE 8.0; Windows NT 5.1; Trident/4.0; Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.1; SV1) ; InfoPath.1; .NET CLR 2.0.50727; .NET CLR 1.1.4322)");

                byte[] response = req.DownloadData(url);

                // Decode and display the response.
                string str = null;
                str = !string.IsNullOrEmpty(encode) ? Encoding.GetEncoding(encode).GetString(response) : req.Encoding.GetString(response);

                req.Dispose();
                return str;
            }
            catch (System.Exception oE)
            {
                File.WriteAllText("RssToEmail.log", "8: error getting " + url + Environment.NewLine + oE + Environment.NewLine);
                return oE.ToString();
            }
        }
    }
}