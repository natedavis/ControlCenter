﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using System.ComponentModel;
using BF2Statistics.ASP;
using BF2Statistics.Database.QueryBuilder;

namespace BF2Statistics.Database
{
    /// <summary>
    /// A class to provide common tasks against the Stats Database
    /// </summary>
    public class StatsDatabase
    {
        /// <summary>
        /// Stats database driver
        /// </summary>
        public DatabaseDriver Driver { get; protected set; }

        /// <summary>
        /// An array of Stats specific table names
        /// </summary>
        public static readonly string[] StatsTables = new string[]
        {
            "army",
            "awards",
            "kills",
            "kits",
            "maps",
            "mapinfo",
            "player",
            "player_history",
            "round_history",
            "servers",
            "unlocks",
            "vehicles",
            "weapons",
        };

        /// <summary>
        /// An array of Player Table names
        /// </summary>
        public static readonly string[] PlayerTables = new string[]
        {
            "army",
            "awards",
            "kills",
            "kits",
            "maps",
            "player",
            "player_history",
            "unlocks",
            "vehicles",
            "weapons",
        };

        /// <summary>
        /// Constructor
        /// </summary>
        public StatsDatabase()
        {
            CheckConnection();
        }

        #region Player Methods

        /// <summary>
        /// Returns whether or not a player exists in the "player" table
        /// </summary>
        /// <param name="Pid">The Player ID</param>
        /// <returns></returns>
        public bool PlayerExists(int Pid)
        {
            return (Driver.Query("SELECT name FROM player WHERE id=@P0", Pid).Count == 1);
        }

        /// <summary>
        /// Returns a list of awards a player has earned
        /// </summary>
        /// <param name="Pid">The Player ID</param>
        /// <returns></returns>
        public List<Dictionary<string, object>> GetPlayerAwards(int Pid)
        {
            CheckConnection();
            return Driver.Query("SELECT awd, level, earned, first FROM awards WHERE id = @P0 ORDER BY id", Pid);
        }

        /// <summary>
        /// Removes a player, based on pid, from the stats database
        /// </summary>
        /// <param name="Pid">The players Id</param>
        /// <param name="TaskFormOpen">
        ///     If true, the task form status message will be updated as progress is made.
        ///     You are still responsible for opening and closing the task form!
        /// </param>
        public void DeletePlayer(int Pid, bool TaskFormOpen)
        {
            CheckConnection();
            DbTransaction Transaction = Driver.BeginTransaction();

            try
            {
                // Remove the player from each player table
                foreach (string Table in PlayerTables)
                {
                    if (TaskFormOpen)
                        TaskForm.UpdateStatus("Removing player from \"" + Table + "\" table...");

                    if (Table == "kills")
                        Driver.Execute(String.Format("DELETE FROM {0} WHERE attacker={1} OR victim={1}", Table, Pid));
                    else
                        Driver.Execute(String.Format("DELETE FROM {0} WHERE id={1}", Table, Pid));
                }

                // Commit Transaction
                if(TaskFormOpen)
                    TaskForm.UpdateStatus("Commiting Transaction");
                Transaction.Commit();
            }
            catch (Exception E)
            {
                // Rollback!
                Transaction.Rollback();
                throw E;
            }
        }

        /// <summary>
        /// Exports a players stats and histroy into an Xml file
        /// </summary>
        /// <param name="XmlPath">The folder path to where the XML will be saved</param>
        /// <param name="Pid">Player ID</param>
        /// <param name="Name">Player Name</param>
        public void ExportPlayerXml(string XmlPath, int Pid, string Name)
        {
            //  Create full path
            string sPath = Path.Combine(
                XmlPath,
                String.Format("{0}_{1}_{2}.xml", Name.Trim().MakeFileNameSafe(), Pid, DateTime.Now.ToString("yyyyMMdd_HHmm"))
            );
            DatabaseDriver Driver = ASPServer.Database.Driver;

            // Delete file if it exists already
            if (File.Exists(sPath))
                File.Delete(sPath);

            // Create XML Settings
            XmlWriterSettings Settings = new XmlWriterSettings();
            Settings.Indent = true;
            Settings.IndentChars = "\t";
            Settings.NewLineChars = Environment.NewLine;
            Settings.NewLineHandling = NewLineHandling.Replace;

            // Write XML data
            using (XmlWriter Writer = XmlWriter.Create(sPath, Settings))
            {
                // Player Element
                Writer.WriteStartDocument();
                Writer.WriteStartElement("Player");

                // Manifest
                Writer.WriteStartElement("Info");
                Writer.WriteElementString("Pid", Pid.ToString());
                Writer.WriteElementString("Name", Name.EscapeXML());
                Writer.WriteElementString("BackupDate", DateTime.Now.ToString());
                Writer.WriteEndElement();

                // Start Tables Element
                Writer.WriteStartElement("TableData");

                // Add each tables data
                foreach (string Table in PlayerTables)
                {
                    // Open table tag
                    Writer.WriteStartElement(Table);

                    // Fetch row
                    List<Dictionary<string, object>> Rows;
                    if (Table == "kills")
                        Rows = Driver.Query(String.Format("SELECT * FROM {0} WHERE attacker={1} OR victim={1}", Table, Pid));
                    else
                        Rows = Driver.Query(String.Format("SELECT * FROM {0} WHERE id={1}", Table, Pid));

                    // Write each row's columns with its value to the xml file
                    foreach (Dictionary<string, object> Row in Rows)
                    {
                        // Open Row tag
                        Writer.WriteStartElement("Row");
                        foreach (KeyValuePair<string, object> Column in Row)
                        {
                            if (Column.Key == "name")
                                Writer.WriteElementString(Column.Key, Column.Value.ToString().EscapeXML());
                            else
                                Writer.WriteElementString(Column.Key, Column.Value.ToString());
                        }

                        // Close Row tag
                        Writer.WriteEndElement();
                    }

                    // Close table tag
                    Writer.WriteEndElement();
                }

                // Close Tags and File
                Writer.WriteEndElement();  // Close Tables Element
                Writer.WriteEndElement();  // Close Player Element
                Writer.WriteEndDocument(); // End and Save file
            }
        }

        /// <summary>
        /// Imports a Player XML Sheet from the specified path
        /// </summary>
        /// <param name="XmlPath">The full path to the XML file</param>
        public void ImportPlayerXml(string XmlPath)
        {
            // Load elements
            XDocument Doc = XDocument.Load(new FileStream(XmlPath, FileMode.Open, FileAccess.Read));
            XElement Info = Doc.Root.Element("Info");
            XElement TableData = Doc.Root.Element("TableData");

            // Make sure player doesnt already exist
            int Pid = Int32.Parse(Info.Element("Pid").Value);
            if (PlayerExists(Pid))
                throw new Exception(String.Format("Player with PID {0} already exists!", Pid));

            // Begin Transaction
            DbTransaction Transaction = Driver.BeginTransaction();

            // Loop through tables
            foreach (XElement Table in TableData.Elements())
            {
                // Loop through Rows
                foreach (XElement Row in Table.Elements())
                {
                    InsertQueryBuilder Query = new InsertQueryBuilder(Table.Name.LocalName, Driver);
                    foreach (XElement Col in Row.Elements())
                    {
                        if (Col.Name.LocalName == "name")
                            Query.SetField(Col.Name.LocalName, Col.Value.UnescapeXML());
                        else
                            Query.SetField(Col.Name.LocalName, Col.Value);
                    }

                    Query.Execute();
                }
            }

            // Commit Transaction
            Transaction.Commit();
        }

        /// <summary>
        /// Imports a players stats from the official gamespy ASP.
        /// This method is to be used in a background worker
        /// </summary>
        public void ImportEaStats(object sender, DoWorkEventArgs e)
        {
            // Setup variables
            BackgroundWorker Worker = (BackgroundWorker)sender;
            int Pid = Int32.Parse(e.Argument.ToString());

            // Make sure redirects are disabled
            if (MainForm.RedirectsEnabled)
                throw new Exception("Cant import player when Gamespy redirects are active");

            // Make sure the player doesnt exist!
            if(PlayerExists(Pid))
                throw new Exception(String.Format("Player with PID {0} already exists!", Pid));

            // Build variables
            Uri GsUrl;
            WebRequest Request;
            HttpWebResponse Response;
            List<string[]> PlayerLines;
            List<string[]> AwardLines;
            List<string[]> MapLines;
            InsertQueryBuilder Query;
            DatabaseDriver Driver = ASPServer.Database.Driver;

            // Create Request
            string Url = String.Format(
                "getplayerinfo.aspx?pid={0}&info=per*,cmb*,twsc,cpcp,cacp,dfcp,kila,heal,rviv,rsup,rpar,tgte,dkas,dsab,cdsc,rank,cmsc,kick,kill,deth,suic,"
                + "ospm,klpm,klpr,dtpr,bksk,wdsk,bbrs,tcdr,ban,dtpm,lbtl,osaa,vrk,tsql,tsqm,tlwf,mvks,vmks,mvn*,vmr*,fkit,fmap,fveh,fwea,wtm-,wkl-,wdt-,"
                + "wac-,wkd-,vtm-,vkl-,vdt-,vkd-,vkr-,atm-,awn-,alo-,abr-,ktm-,kkl-,kdt-,kkd-",
                Pid);
            Worker.ReportProgress(1, "Requesting Player Stats");
            GsUrl = new Uri("http://bf2web.gamespy.com/ASP/" + Url);
            Request = WebRequest.Create(GsUrl);

            // Get response
            Response = (HttpWebResponse)Request.GetResponse();
            if (Response.StatusCode != HttpStatusCode.OK)
                throw new Exception("Unable to connect to the Gamespy ASP Webservice!");

            // Read response data
            Worker.ReportProgress(2, "Parsing Stats Response");
            PlayerLines = new List<string[]>();
            using (StreamReader Reader = new StreamReader(Response.GetResponseStream()))
                while(!Reader.EndOfStream)
                    PlayerLines.Add(Reader.ReadLine().Split('\t'));

            // Does the player exist?
            if (PlayerLines[0][0] != "O")
                throw new Exception("Player does not exist on the gamespy servers!");

            // Fetch player mapinfo
            Worker.ReportProgress(3, "Requesting Player Map Data");
            GsUrl = new Uri(String.Format("http://bf2web.gamespy.com/ASP/getplayerinfo.aspx?pid={0}&info=mtm-,mwn-,mls-", Pid));
            Request = WebRequest.Create(GsUrl);

            // Get response
            Response = (HttpWebResponse)Request.GetResponse();
            if (Response.StatusCode != HttpStatusCode.OK)
                throw new Exception("Unable to connect to the Gamespy ASP Webservice!");

            // Read response data
            Worker.ReportProgress(4, "Parsing Map Data Response");
            MapLines = new List<string[]>();
            using (StreamReader Reader = new StreamReader(Response.GetResponseStream()))
                while(!Reader.EndOfStream)
                    MapLines.Add(Reader.ReadLine().Split('\t'));

            // Fetch player awards
            Worker.ReportProgress(5, "Requesting Player Awards");
            GsUrl = new Uri(String.Format("http://bf2web.gamespy.com/ASP/getawardsinfo.aspx?pid={0}", Pid));
            Request = WebRequest.Create(GsUrl);

            // Get response
            Response = (HttpWebResponse)Request.GetResponse();
            if (Response.StatusCode != HttpStatusCode.OK)
                throw new Exception("Unable to connect to the Gamespy ASP Webservice!");

            // Read response data
            Worker.ReportProgress(6, "Parsing Player Awards Response");
            AwardLines = new List<string[]>();
            using (StreamReader Reader = new StreamReader(Response.GetResponseStream()))
                while(!Reader.EndOfStream)
                    AwardLines.Add(Reader.ReadLine().Split('\t'));

            // === Process Player Info === //

            // Parse Player Data
            Dictionary<string, string> PlayerInfo = StatsParser.ParseHeaderData(PlayerLines[3], PlayerLines[4]);
            int Rounds = Int32.Parse(PlayerInfo["mode0"]) + Int32.Parse(PlayerInfo["mode1"]) + Int32.Parse(PlayerInfo["mode2"]);

            // Begin database transaction
            DbTransaction Transaction = Driver.BeginTransaction();

            // Wrap all DB inserts into a try block so we can rollback on error
            try
            {
                // Insert Player Data
                Worker.ReportProgress(7, "Inserting Player Data Into Table: player");
                Query = new InsertQueryBuilder("player", Driver);
                Query.SetField("id", Pid);
                Query.SetField("name", " " + PlayerInfo["nick"].Trim()); // Online accounts always start with space in bf2stats
                Query.SetField("country", "xx");
                Query.SetField("time", PlayerInfo["time"]);
                Query.SetField("rounds", Rounds);
                Query.SetField("ip", "127.0.0.1");
                Query.SetField("score", PlayerInfo["scor"]);
                Query.SetField("cmdscore", PlayerInfo["cdsc"]);
                Query.SetField("skillscore", PlayerInfo["cmsc"]);
                Query.SetField("teamscore", PlayerInfo["twsc"]);
                Query.SetField("kills", PlayerInfo["kill"]);
                Query.SetField("deaths", PlayerInfo["deth"]);
                Query.SetField("captures", PlayerInfo["cpcp"]);
                Query.SetField("captureassists", PlayerInfo["cacp"]);
                Query.SetField("defends", PlayerInfo["dfcp"]);
                Query.SetField("damageassists", PlayerInfo["kila"]);
                Query.SetField("heals", PlayerInfo["heal"]);
                Query.SetField("revives", PlayerInfo["rviv"]);
                Query.SetField("ammos", PlayerInfo["rsup"]);
                Query.SetField("repairs", PlayerInfo["rpar"]);
                Query.SetField("driverspecials", PlayerInfo["dsab"]);
                Query.SetField("suicides", PlayerInfo["suic"]);
                Query.SetField("killstreak", PlayerInfo["bksk"]);
                Query.SetField("deathstreak", PlayerInfo["wdsk"]);
                Query.SetField("rank", PlayerInfo["rank"]);
                Query.SetField("banned", PlayerInfo["ban"]);
                Query.SetField("kicked", PlayerInfo["kick"]);
                Query.SetField("cmdtime", PlayerInfo["tcdr"]);
                Query.SetField("sqltime", PlayerInfo["tsql"]);
                Query.SetField("sqmtime", PlayerInfo["tsqm"]);
                Query.SetField("lwtime", PlayerInfo["tlwf"]);
                Query.SetField("wins", PlayerInfo["wins"]);
                Query.SetField("losses", PlayerInfo["loss"]);
                Query.SetField("joined", PlayerInfo["jond"]);
                Query.SetField("rndscore", PlayerInfo["bbrs"]);
                Query.SetField("lastonline", PlayerInfo["lbtl"]);
                Query.SetField("mode0", PlayerInfo["mode0"]);
                Query.SetField("mode1", PlayerInfo["mode1"]);
                Query.SetField("mode2", PlayerInfo["mode2"]);
                Query.Execute();

                // Insert Army Data
                Worker.ReportProgress(8, "Inserting Player Data Into Table: army");
                Query = new InsertQueryBuilder("army", Driver);
                Query.SetField("id", Pid);
                for (int i = 0; i < 10; i++)
                {
                    Query.SetField("time" + i, PlayerInfo["atm-" + i]);
                    Query.SetField("win" + i, PlayerInfo["awn-" + i]);
                    Query.SetField("loss" + i, PlayerInfo["alo-" + i]);
                    Query.SetField("best" + i, PlayerInfo["abr-" + i]);
                }
                Query.Execute();

                // Insert Kit Data
                Worker.ReportProgress(9, "Inserting Player Data Into Table: kits");
                Query = new InsertQueryBuilder("kits", Driver);
                Query.SetField("id", Pid);
                for (int i = 0; i < 7; i++)
                {
                    Query.SetField("time" + i, PlayerInfo["ktm-" + i]);
                    Query.SetField("kills" + i, PlayerInfo["kkl-" + i]);
                    Query.SetField("deaths" + i, PlayerInfo["kdt-" + i]);
                }
                Query.Execute();

                // Insert Vehicle Data
                Worker.ReportProgress(10, "Inserting Player Data Into Table: vehicles");
                Query = new InsertQueryBuilder("vehicles", Driver);
                Query.SetField("id", Pid);
                Query.SetField("timepara", 0);
                for (int i = 0; i < 7; i++)
                {
                    Query.SetField("time" + i, PlayerInfo["vtm-" + i]);
                    Query.SetField("kills" + i, PlayerInfo["vkl-" + i]);
                    Query.SetField("deaths" + i, PlayerInfo["vdt-" + i]);
                    Query.SetField("rk" + i, PlayerInfo["vkr-" + i]);
                }
                Query.Execute();

                // Insert Weapon Data
                Worker.ReportProgress(11, "Inserting Player Data Into Table: weapons");
                Query = new InsertQueryBuilder("weapons", Driver);
                Query.SetField("id", Pid);
                for (int i = 0; i < 9; i++)
                {
                    Query.SetField("time" + i, PlayerInfo["wtm-" + i]);
                    Query.SetField("kills" + i, PlayerInfo["wkl-" + i]);
                    Query.SetField("deaths" + i, PlayerInfo["wdt-" + i]);
                }

                // Knife
                Query.SetField("knifetime", PlayerInfo["wtm-9"]);
                Query.SetField("knifekills", PlayerInfo["wkl-9"]);
                Query.SetField("knifedeaths", PlayerInfo["wdt-9"]);
                // Shockpad
                Query.SetField("shockpadtime", PlayerInfo["wtm-10"]);
                Query.SetField("shockpadkills", PlayerInfo["wkl-10"]);
                Query.SetField("shockpaddeaths", PlayerInfo["wdt-10"]);
                // Claymore
                Query.SetField("claymoretime", PlayerInfo["wtm-11"]);
                Query.SetField("claymorekills", PlayerInfo["wkl-11"]);
                Query.SetField("claymoredeaths", PlayerInfo["wdt-11"]);
                // Handgrenade
                Query.SetField("handgrenadetime", PlayerInfo["wtm-12"]);
                Query.SetField("handgrenadekills", PlayerInfo["wkl-12"]);
                Query.SetField("handgrenadedeaths", PlayerInfo["wdt-12"]);
                // SF Weapn Data
                Query.SetField("tacticaldeployed", PlayerInfo["de-6"]);
                Query.SetField("grapplinghookdeployed", PlayerInfo["de-7"]);
                Query.SetField("ziplinedeployed", PlayerInfo["de-8"]);

                Query.Execute();

                // === Process Awards Data === //
                Worker.ReportProgress(12, "Inserting Player Awards");
                List<Dictionary<string, string>> Awards = StatsParser.ParseAwards(AwardLines);
                foreach (Dictionary<string, string> Award in Awards)
                {
                    Query = new InsertQueryBuilder("awards", Driver);
                    Query.SetField("id", Pid);
                    Query.SetField("awd", Award["id"]);
                    Query.SetField("level", Award["level"]);
                    Query.SetField("earned", Award["when"]);
                    Query.SetField("first", Award["first"]);
                    Query.Execute();
                }

                // === Process Map Data === //
                Worker.ReportProgress(13, "Inserting Player Map Data");
                PlayerInfo = StatsParser.ParseHeaderData(MapLines[3], MapLines[4]);
                int[] Maps = new int[] { 
                    0, 1, 2, 3, 4, 5, 6, 100, 101, 102, 103, 105, 
                    601, 300, 301, 302, 303, 304, 305, 306, 307, 
                    10, 11, 110, 200, 201, 202, 12 
                };
                foreach (int MapId in Maps)
                {
                    if (PlayerInfo.ContainsKey("mtm-" + MapId))
                    {
                        Query = new InsertQueryBuilder("maps", Driver);
                        Query.SetField("id", Pid);
                        Query.SetField("mapid", MapId);
                        Query.SetField("time", PlayerInfo["mtm-" + MapId]);
                        Query.SetField("win", PlayerInfo["mwn-" + MapId]);
                        Query.SetField("loss", PlayerInfo["mls-" + MapId]);
                        Query.SetField("best", 0);
                        Query.SetField("worst", 0);
                        Query.Execute();
                    }
                }

                // Commit transaction
                Transaction.Commit();
            }
            catch (Exception E)
            {
                Transaction.Rollback();
                throw E;
            }
        } 

        #endregion Player Methods

        /// <summary>
        /// Clears all stats data from the stats database
        /// </summary>
        public void Truncate()
        {
            // Sqlite Database doesnt have a truncate method, so we will just recreate the database
            if (Driver.DatabaseEngine == DatabaseEngine.Sqlite)
            {
                // Stop the server to delete the file
                ASPServer.Stop();
                File.Delete(Path.Combine(MainForm.Root, MainForm.Config.StatsDBName + ".sqlite3"));
                System.Threading.Thread.Sleep(500); // Make sure the file deletes before the ASP server starts again!

                // Reset driver, start ASP again
                Driver = null;
                ASPServer.Start();
            }
            else
            {
                // Use MySQL's truncate method to clear the tables;
                foreach (string Table in StatsTables)
                    Driver.Execute("TRUNCATE TABLE " + Table);
            }
        }

        /// <summary>
        /// Creates the connection to the database, and handles
        /// the excpetion (if any) that are thrown
        /// </summary>
        /// <summary>
        /// Creates the connection to the database, and handles
        /// the excpetion (if any) that are thrown
        /// </summary>
        public void CheckConnection()
        {
            if (Driver == null || !Driver.IsConnected)
            {
                try
                {
                    // First time connection
                    if (Driver == null)
                    {
                        // Create database connection
                        Driver = new DatabaseDriver(
                            MainForm.Config.StatsDBEngine,
                            MainForm.Config.StatsDBHost,
                            MainForm.Config.StatsDBPort,
                            MainForm.Config.StatsDBName,
                            MainForm.Config.StatsDBUser,
                            MainForm.Config.StatsDBPass
                        );
                        Driver.Connect();

                        // Create SQL tables on new SQLite DB's
                        if (Driver.IsNewDatabase)
                        {
                            CreateSqliteTables(Driver);
                            return;
                        }
                        else
                        {
                            // Try and get database version
                            try
                            {
                                var Rows = Driver.Query("SELECT dbver FROM _version LIMIT 1");
                                if (Rows.Count == 0)
                                    throw new Exception(); // Force insert of IP2Nation
                            }
                            catch
                            {
                                // Table doesnt contain a _version table, so run the createTables.sql
                                if (Driver.DatabaseEngine == DatabaseEngine.Sqlite)
                                    CreateSqliteTables(Driver);
                                else
                                    CreateMysqlTables(Driver);
                            }

                            return;
                        }
                    }

                    // Connect to DB
                    Driver.Connect();

                    // Set global packet size with MySql
                    if (Driver.DatabaseEngine == DatabaseEngine.Mysql)
                        Driver.Execute("SET GLOBAL max_allowed_packet=51200");
                }
                catch (Exception E)
                {
                    throw new Exception(
                        "Database Connect Error: " +
                        Environment.NewLine +
                        E.Message +
                        Environment.NewLine +
                        "Forcing Server Shutdown..."
                    );
                }
            }
        }

        /// <summary>
        /// On a new Sqlite database, this method will create the default tables
        /// </summary>
        /// <param name="Driver"></param>
        private void CreateSqliteTables(DatabaseDriver Driver)
        {
            // Show Progress Form
            MainForm.Disable();
            bool TaskFormWasOpen = TaskForm.IsOpen;
            if(!TaskFormWasOpen)
                TaskForm.Show(MainForm.Instance, "Create Database", "Creating Bf2Stats SQLite Database...", false);

            // Create Tables
            TaskForm.UpdateStatus("Creating Stats Tables");
            string SQL = Utils.GetResourceAsString("BF2Statistics.SQL.SQLite.Stats.sql");
            Driver.Execute(SQL);

            // Insert Ip2Nation data
            TaskForm.UpdateStatus("Inserting Ip2Nation Data");
            SQL = Utils.GetResourceAsString("BF2Statistics.SQL.Ip2nation.sql");
            DbTransaction Transaction = Driver.BeginTransaction();
            Driver.Execute(SQL);

            // Attempt to do the transaction
            try
            {
                Transaction.Commit();
            }
            catch (Exception E)
            {
                Transaction.Rollback();
                if(!TaskFormWasOpen)
                    TaskForm.CloseForm();
                MainForm.Enable();
                throw E;
            }

            // Close update progress form
            if(!TaskFormWasOpen) TaskForm.CloseForm();
            MainForm.Enable();
        }

        /// <summary>
        /// On a new Mysql database, this method will create the default tables
        /// </summary>
        /// <param name="Driver"></param>
        private void CreateMysqlTables(DatabaseDriver Driver)
        {
            // Show Progress Form
            MainForm.Disable();
            bool TaskFormWasOpen = TaskForm.IsOpen;
            if (!TaskFormWasOpen)
                TaskForm.Show(MainForm.Instance, "Create Database", "Creating Bf2Stats Mysql Tables...", false);

            // To prevent packet size errors
            Driver.Execute("SET GLOBAL max_allowed_packet=51200");

            // Start Transaction
            DbTransaction Transaction = Driver.BeginTransaction();
            TaskForm.UpdateStatus("Creating Stats Tables");

            // Gets Table Queries
            string[] SQL = Utils.GetResourceFileLines("BF2Statistics.SQL.MySQL.Stats.sql");
            List<string> Queries = Utilities.Sql.ExtractQueries(SQL);

            // Attempt to do the transaction
            try
            {
                // Create Tables
                foreach (string Query in Queries)
                    Driver.Execute(Query);

                // Commit
                Transaction.Commit();
            }
            catch (Exception E)
            {
                Transaction.Rollback();
                if (!TaskFormWasOpen)
                    TaskForm.CloseForm();
                MainForm.Enable();
                throw E;
            }

            // Insert Ip2Nation data
            Transaction = Driver.BeginTransaction();
            TaskForm.UpdateStatus("Inserting Ip2Nation Data");
            SQL = Utils.GetResourceFileLines("BF2Statistics.SQL.Ip2nation.sql");
            Queries = Utilities.Sql.ExtractQueries(SQL);

            // Attempt to do the transaction
            try
            {
                // Insert rows
                foreach (string Query in Queries)
                    Driver.Execute(Query);

                // Commit
                Transaction.Commit();
            }
            catch (Exception E)
            {
                Transaction.Rollback();
                if(!TaskFormWasOpen)
                    TaskForm.CloseForm();
                MainForm.Enable();
                throw E;
            }

            // Close update progress form
            if (!TaskFormWasOpen) TaskForm.CloseForm();
            MainForm.Enable();
        }

        /// <summary>
        /// Closes the database connection
        /// </summary>
        public void Close()
        {
            if (Driver != null)
                Driver.Close();
        }
    }
}
