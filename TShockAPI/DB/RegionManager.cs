﻿/*   
TShock, a server mod for Terraria
Copyright (C) 2011 The TShock Team

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.IO;
using System.Data;
using TShockAPI.DB;
using Community.CsharpSqlite.SQLiteClient;
using Microsoft.Xna.Framework;
using Terraria;


namespace TShockAPI.DB
{
    public class RegionManager
    {
        public List<Region> Regions = new List<Region>();

        private IDbConnection database;

        public RegionManager(IDbConnection db)
        {
            database = db;

            using (var com = database.CreateCommand())
            {
                string query = (TShock.Config.StorageType.ToLower() == "sqlite") ?
                    "CREATE TABLE IF NOT EXISTS 'Regions' ('X1' NUMERIC, 'Y1' NUMERIC, 'height' NUMERIC, 'width' NUMERIC, 'RegionName' TEXT PRIMARY KEY, 'WorldID' TEXT, 'UserIds' TEXT, 'Protected' NUMERIC);":
                        "CREATE TABLE IF NOT EXISTS Regions (X1 INT(11), Y1 INT(11), height INT(11), width INT(11), RegionName VARCHAR(255) PRIMARY, WorldID VARCHAR(255), UserIds VARCHAR(255), Protected INT(1));";

                database.Query(query);


                ImportOld();
            }
        }

        public void ImportOld()
        {
            String file = Path.Combine(TShock.SavePath, "regions.xml");
            if (!File.Exists(file))
                return;

            Region region;
            Rectangle rect;
            using (var sr = new StreamReader(file))
            {
                using (var reader = XmlReader.Create(sr))
                {
                    // Parse the file and display each of the nodes.
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Name != "ProtectedRegion")
                            continue;

                        region = new Region();
                        rect = new Rectangle();

                        bool endregion = false;
                        while (reader.Read() && !endregion)
                        {
                            if (reader.NodeType != XmlNodeType.Element)
                                continue;

                            string name = reader.Name;

                            while (reader.Read() && reader.NodeType != XmlNodeType.Text) ;

                            switch (name)
                            {
                                case "RegionName":
                                    region.Name = reader.Value;
                                    break;
                                case "Point1X":
                                    int.TryParse(reader.Value, out rect.X);
                                    break;
                                case "Point1Y":
                                    int.TryParse(reader.Value, out rect.Y);
                                    break;
                                case "Point2X":
                                    int.TryParse(reader.Value, out rect.Width);
                                    break;
                                case "Point2Y":
                                    int.TryParse(reader.Value, out rect.Height);
                                    break;
                                case "Protected":
                                    region.DisableBuild = reader.Value.ToLower().Equals("true");
                                    break;
                                case "WorldName":
                                    region.WorldID = reader.Value;
                                    break;
                                case "AllowedUserCount":
                                    break;
                                case "IP":
                                    region.AllowedIDs.Add(int.Parse(reader.Value));
                                    break;
                                default:
                                    endregion = true;
                                    break;
                            }
                        }

                        region.Area = rect;
                        using (var com = database.CreateCommand())
                        {
                            if (TShock.Config.StorageType.ToLower() == "sqlite")
                                com.CommandText =
                                    "INSERT OR IGNORE INTO Regions VALUES (@tx, @ty, @height, @width, @name, @worldid, @userids, @protected);";
                            else if (TShock.Config.StorageType.ToLower() == "mysql")
                                com.CommandText =
                                    "INSERT IGNORE INTO Regions SET X1=@tx, Y1=@ty, height=@height, width=@width, RegionName=@name, WorldID=@world, UserIds=@userids, Protected=@protected;";
                            com.AddParameter("@tx", region.Area.X);
                            com.AddParameter("@ty", region.Area.Y);
                            com.AddParameter("@width", region.Area.Width);
                            com.AddParameter("@height", region.Area.Height);
                            com.AddParameter("@name", region.Name);
                            com.AddParameter("@worldid", region.WorldID);
                            //Todo: What should this be? We don't really have a way to go from ips to userids
                            com.AddParameter("@userids", ""/*string.Join(",", region.AllowedIDs)*/);
                            com.AddParameter("@protected", region.DisableBuild);
                            int num = com.ExecuteNonQuery();
                        }
                    }
                }
            }
            String path = Path.Combine(TShock.SavePath, "old_configs");
            String file2 = Path.Combine(path, "regions.xml");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            if (File.Exists(file2))
                File.Delete(file2);
            File.Move(file, file2);

            ReloadAllRegions();
        }

        public void ConvertDB()
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "UPDATE Regions SET WorldID=@worldid";
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    com.ExecuteNonQuery();
                    com.Parameters.Clear();
                    com.CommandText = "UPDATE Regions SET UserIds=@UserIds";
                    com.AddParameter("@UserIds", "");
                    com.ExecuteNonQuery();
                    ReloadAllRegions();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        public void ReloadAllRegions()
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "SELECT * FROM Regions WHERE WorldID=@worldid";
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    using (var reader = com.ExecuteReader())
                    {
                        Regions.Clear();
                        while (reader.Read())
                        {
                            int X1 = reader.Get<int>("X1");
                            int Y1 = reader.Get<int>("Y1");
                            int height = reader.Get<int>("height");
                            int width = reader.Get<int>("width");
                            int Protected = reader.Get<int>("Protected");
                            string MergedIDs = DbExt.Get<string>(reader, "UserIds");
                            string name = DbExt.Get<string>(reader, "RegionName");

                            string[] SplitIDs = MergedIDs.Split(',');

                            Region r = new Region(new Rectangle(X1, Y1, width, height), name, Protected != 0, Main.worldID.ToString());
                            try
                            {
                                for (int i = 0; i < SplitIDs.Length; i++)
                                {
                                    r.AllowedIDs.Add(Convert.ToInt32(SplitIDs[i]));
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error("Your database contains invalid UserIDs (they should be ints).");
                                Log.Error("A lot of things will fail because of this. You must manually delete and re-create the allowed field.");
                                Log.Error(e.Message);
                                Log.Error(e.StackTrace);
                            }

                            Regions.Add(r);
                        }
                        reader.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
        }

        public void ReloadForUnitTest(String n)
        {
            using (var com = database.CreateCommand())
            {
                com.CommandText = "SELECT * FROM Regions WHERE WorldID=@worldid";
                com.AddParameter("@worldid", n);
                using (var reader = com.ExecuteReader())
                {
                    Regions.Clear();
                    while (reader.Read())
                    {
                        int X1 = reader.Get<int>("X1");
                        int Y1 = reader.Get<int>("Y1");
                        int height = reader.Get<int>("height");
                        int width = reader.Get<int>("width");
                        int Protected = reader.Get<int>("Protected");
                        string MergedIDs = DbExt.Get<string>(reader, "UserIds");
                        string name = DbExt.Get<string>(reader, "RegionName");
                        string[] SplitIDs = MergedIDs.Split(',');

                        Region r = new Region(new Rectangle(X1, Y1, width, height), name, Protected != 0, Main.worldID.ToString());
                        try
                        {
                            for (int i = 0; i < SplitIDs.Length; i++)
                            {
                                r.AllowedIDs[i] = Convert.ToInt32(SplitIDs[i]);
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error("Your database contains invalid UserIDs (they should be ints).");
                            Log.Error("A lot of things will fail because of this. You must manually delete and re-create the allowed field.");
                            Log.Error(e.Message);
                            Log.Error(e.StackTrace);
                        }

                        Regions.Add(r);
                    }
                    reader.Close();
                }
            }
        }

        public bool AddRegion(int tx, int ty, int width, int height, string regionname, string worldid)
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText =
                        "INSERT INTO Regions VALUES (@tx, @ty, @height, @width, @name, @worldid, @userids, @protected);";
                    com.AddParameter("@tx", tx);
                    com.AddParameter("@ty", ty);
                    com.AddParameter("@width", width);
                    com.AddParameter("@height", height);
                    com.AddParameter("@name", regionname.ToLower());
                    com.AddParameter("@worldid", worldid);
                    com.AddParameter("@userids", "");
                    com.AddParameter("@protected", 1);
                    com.ExecuteNonQuery();
                    Regions.Add(new Region(new Rectangle(tx, ty, width, height), regionname, true, worldid));
                    return true;

                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
            return false;
        }

        public bool DeleteRegion(string name)
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "DELETE FROM Regions WHERE RegionName=@name AND WorldID=@worldid";
                    com.AddParameter("@name", name.ToLower());
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    com.ExecuteNonQuery();
                    Regions.Remove(getRegion(name));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
            return false;
        }

        public bool SetRegionState(string name, bool state)
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "UPDATE Regions SET Protected=@bool WHERE RegionName=@name AND WorldID=@worldid";
                    com.AddParameter("@name", name);
                    com.AddParameter("@bool", state ? 1 : 0);
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    com.ExecuteNonQuery();
                    getRegion(name).DisableBuild = state;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return false;
            }
        }

        public bool SetRegionStateTest(string name, string world, bool state)
        {
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "UPDATE Regions SET Protected=@bool WHERE RegionName=@name AND WorldID=@worldid";
                    com.AddParameter("@name", name);
                    com.AddParameter("@bool", state ? 1 : 0);
                    com.AddParameter("@worldid", world);
                    com.ExecuteNonQuery();
                    getRegion(name).DisableBuild = state;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return false;
            }
        }

        public bool CanBuild(int x, int y, TSPlayer ply)
        {
            if (!ply.Group.HasPermission("canbuild"))
            {
                return false;
            }
            for (int i = 0; i < Regions.Count; i++)
            {
                if (Regions[i].InArea(new Rectangle(x, y, 0, 0)) && !Regions[i].HasPermissionToBuildInRegion(ply))
                {
                    return false;
                }
            }
            return true;
        }

        public bool InArea(int x, int y)
        {
            foreach (Region region in Regions)
            {
                if (x >= region.Area.Left && x <= region.Area.Right &&
                    y >= region.Area.Top && y <= region.Area.Bottom &&
                    region.DisableBuild)
                {
                    return true;
                }
            }
            return false;
        }

        public static List<string> ListIDs(string MergedIDs)
        {
            List<string> SplitIDs = new List<string>();
            /*var sb = new StringBuilder();
            for (int i = 0; i < MergedIDs.Length; i++)
            {
                char c = MergedIDs[i];

                if (c != ',')
                {
                    sb.Append(c);
                }
                else if (sb.Length > 0)
                {
                    SplitIDs.Add(sb.ToString());
                    sb.Clear();
                }
            }*/
            String[] s = MergedIDs.Split(',');
            for( int i = 0; i < s.Length; i++ )
            {
                if (!s[i].Equals(""))
                    SplitIDs.Add(s[i]);
            }
            return SplitIDs;
        }

        public bool AddNewUser(string regionName, String userName)
        {


            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "SELECT * FROM Regions WHERE RegionName=@name AND WorldID=@worldid";
                    com.AddParameter("@name", regionName);
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    string MergedIDs = string.Empty;
                    using (var reader = com.ExecuteReader())
                    {
                        if (reader.Read())
                            MergedIDs = reader.Get<string>("UserIds");
                    }

                    if (MergedIDs == string.Empty)
                        MergedIDs = Convert.ToString(TShock.Users.GetUserID(userName));
                    else
                        MergedIDs = MergedIDs + "," + Convert.ToString(TShock.Users.GetUserID(userName));
                    com.Parameters.Clear();
                    com.CommandText = "UPDATE Regions SET UserIds=@ids WHERE RegionName=@name AND WorldID=@worldid";
                    com.AddParameter("@ids", MergedIDs);
                    com.AddParameter("@name", regionName);
                    com.AddParameter("@worldid", Main.worldID.ToString());
                    if (com.ExecuteNonQuery() > 0)
                    {
                        ReloadAllRegions();
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
                return false;
            }
        }

        /// <summary>
        /// Gets all the regions names from world
        /// </summary>
        /// <param name="worldid">World name to get regions from</param>
        /// <returns>List of regions with only their names</returns>
        public List<Region> ListAllRegions(string worldid)
        {
            var regions = new List<Region>();
            try
            {
                using (var com = database.CreateCommand())
                {
                    com.CommandText = "SELECT RegionName FROM Regions WHERE WorldID=@worldid";
                    com.AddParameter("@worldid", worldid);
                    using (var reader = com.ExecuteReader())
                    {
                        while (reader.Read())
                            regions.Add(new Region { Name = reader.Get<string>("RegionName") });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex.ToString());
            }
            return regions;
        }

        public Region getRegion(String name)
        {
            foreach (Region r in Regions)
            {
                if (r.Name.Equals(name))
                    return r;
            }
            return new Region();
        }
    }

    public class Region
    {
        public Rectangle Area { get; set; }
        public string Name { get; set; }
        public bool DisableBuild { get; set; }
        public string WorldID { get; set; }
        public List<int> AllowedIDs { get; set; }

        public Region(Rectangle region, string name, bool disablebuild, string RegionWorldIDz)
            :this()
        {
            Area = region;
            Name = name;
            DisableBuild = disablebuild;
            WorldID = RegionWorldIDz;
        }

        public Region()
        {
            Area = Rectangle.Empty;
            Name = string.Empty;
            DisableBuild = true;
            WorldID = string.Empty;
            AllowedIDs = new List<int>();
        }

        public bool InArea(Rectangle point)
        {
            if (Area.Contains(point.X, point.Y))
            {
                return true;
            }
            return false;
        }

        public bool HasPermissionToBuildInRegion(TSPlayer ply)
        {
            if (!ply.IsLoggedIn)
            {
                if (!ply.HasBeenNaggedAboutLoggingIn)
                {
                    ply.SendMessage("You must be logged in to take advantage of protected regions.", Color.Red);
                    ply.HasBeenNaggedAboutLoggingIn = true;
                }
                return false;
            }
            if (!DisableBuild)
            {
                return true;
            }

            for (int i = 0; i < AllowedIDs.Count; i++)
            {
                if (AllowedIDs[i] == ply.UserID)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
