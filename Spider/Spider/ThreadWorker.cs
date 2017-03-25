﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Spider
{
    class ThreadWorker
    {
        ConcurrentQueue<string> Scheduled_links;
        DBController Database;
        GUI reporter;

        public ThreadWorker(ConcurrentQueue<string> SL, GUI RP)
        {
            this.Scheduled_links = SL;
            this.Database = DBController.GetInstance();
            this.reporter = RP;
        }
        
        public void ThreadProc(object obj)
        {
            if (Controller.OperationCancelled)
                return;

            string link = (string)obj;
            bool Revisted = false;

            if (Database.LinkExists(link))
            {
                Database.LinkHit(link);

                if ((DateTime.Now - Database.GetLinkDate(link).GetValueOrDefault()).TotalDays > 7)
                {
                    Revisted = true;
                }
                else
                {
                    return;
                }
            }

            if (!RobotstxtParser.Approved(link))
            {
                return;
            }


            if (Controller.OperationCancelled)
                return;

            reporter.Invoke(reporter.ReportStartProcessing, link);

            string html = HtmlDownloader.GetHtml(link);

            if (Controller.OperationCancelled)
                return;


            if (html == null)
                return;

            HtmlParser doc = new HtmlParser(html, link);

            int linkscount = 0;


            if (!Revisted)
            {
                IEnumerable<string> links_list = doc.GetOutGoingLinks();
                HashSet<string> Distinct = new HashSet<string>(links_list);
                StringBuilder queued = new StringBuilder();
                

                foreach (string Link in Distinct)
                {
                    if (!Link.Contains("#"))
                    {
                        linkscount++;

                        Scheduled_links.Enqueue(Link);


                        if (Controller.OperationCancelled)
                            return;

                        queued.Append(Link);
                        queued.Append(Environment.NewLine);

                    }

                }

                reporter.Invoke(reporter.ReportQueued, queued.ToString());
            }

            string title = doc.GetTitle();

            string PlainText = doc.PlainText();

            Dictionary<string, int> dictionary = doc.KeywordsVectors();

            if (dictionary == null)
                return;


            if (Controller.OperationCancelled)
                return;


            //redundant re check but needed because race condition may occuar leading to deplicate entry
            if (Database.LinkExists(link) && !Revisted)
            {
                Database.LinkHit(link);
                return;
            }
            else if (Revisted)
            {
                Database.UpdateLinkDate(link);
                Database.UpdateLinkTitle(link, title);
                Database.UpdatePageVector(link, dictionary);
                Database.UpdatePageContent(link, PlainText);
            }
            else
            {
                Database.AddLink(link, title, linkscount);
                Database.AddPageVector(link, dictionary);
                Database.AddPageContent(link, PlainText);
            }


            if (Controller.OperationCancelled)
                return;
          
            reporter.Invoke(reporter.ReportStatistics, dictionary);
 
            reporter.Invoke(reporter.ReportProcessed, link);

        }

    }
}
