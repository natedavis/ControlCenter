﻿using System;
using System.Collections.Generic;
using BF2Statistics.Database;

namespace BF2Statistics.Web.ASP
{
    class RankNotification
    {
        /// <summary>
        /// This request clears all rank announcements for a specific player
        /// </summary>
        /// <queryParam name="pid" type="int">The unique player ID</queryParam>
        /// <param name="Client">The HttpClient who made the request</param>
        /// <param name="Driver">The Stats Database Driver. Connection errors are handled in the calling object</param>
        public RankNotification(HttpClient Client, StatsDatabase Driver)
        {
            int Pid = 0;
            List<Dictionary<string, object>> Rows;
            ASPResponse Response = Client.Response as ASPResponse;

            // Setup Params
            if (Client.Request.QueryString.ContainsKey("pid"))
                Int32.TryParse(Client.Request.QueryString["pid"], out Pid);

            // Fetch Player
            Rows = Driver.Query("SELECT rank FROM player WHERE id=@P0", Pid);
            if (Rows.Count == 0)
            {
                Response.WriteResponseStart(false);
                Response.WriteFreeformLine("Player Doesnt Exist!");
                Client.Response.Send();
                return;
            }

            // Reset
            Driver.Execute("UPDATE player SET chng=0, decr=0 WHERE id=@P0", Pid);
            Response.WriteResponseStart();
            Response.WriteFreeformLine(String.Format("Cleared rank notification {0}", Pid));
            Response.Send();
        }
    }
}
