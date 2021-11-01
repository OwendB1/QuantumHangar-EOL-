﻿using Newtonsoft.Json;
using NLog;
using QuantumHangar.Serialization;
using QuantumHangar.Utilities;
using Sandbox;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Blocks;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;

namespace QuantumHangar.HangarMarket
{
    public class HangarMarketController
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        //files will by .json 


        private static string MarketFolderDir;




        public static ConcurrentDictionary<string, MarketListing> MarketOffers = new ConcurrentDictionary<string, MarketListing>();




        // We use this to read new offers
        private FileSystemWatcher MarketWatcher;
        private ClientCommunication Communication;
       
        private static MethodInfo SendNewProjection;
   

        public HangarMarketController()
        {
            //Run this when server initilizes
            MarketFolderDir = Path.Combine(Hangar.Config.FolderDirectory, "HangarMarket");


            //Make sure to create the market directory
            Directory.CreateDirectory(MarketFolderDir);


            //Initilize server and read all exsisting market files
            string[] MarketFileOffers = Directory.GetFiles(MarketFolderDir, "*.json", SearchOption.TopDirectoryOnly);
            foreach (var OfferPath in MarketFileOffers)
            {
                string FileName = Path.GetFileName(OfferPath);
                Log.Error($"Adding File: {FileName}");


                if (!TryGetReadMarketFile(OfferPath, out MarketListing Offer))
                    continue;

                MarketOffers.TryAdd(FileName, Offer);
            }






            //Read all grids and get their objectbuilders serialized and ready...

            //Create fileSystemWatcher

            MarketWatcher = new FileSystemWatcher(MarketFolderDir);
            MarketWatcher.NotifyFilter = NotifyFilters.Attributes | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Security | NotifyFilters.FileName;


            

            MarketWatcher.Created += MarketWatcher_Created;
            MarketWatcher.Deleted += MarketWatcher_Deleted;
            MarketWatcher.Changed += MarketWatcher_Changed;
            MarketWatcher.Renamed += MarketWatcher_Renamed;


            MarketWatcher.IncludeSubdirectories = true;
            MarketWatcher.EnableRaisingEvents = true;




           
            SendNewProjection = typeof(MyProjectorBase).GetMethod("SendNewBlueprint", BindingFlags.NonPublic | BindingFlags.Instance);

            
        }


        public void ServerStarted()
        {
            Communication = new ClientCommunication();
        }

        public void Close()
        {
            Communication.close();
        }

        private void MarketWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            Log.Warn($"File {e.FullPath} renamed!");
        }

        private void MarketWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            //Market offer changed
            if (e.ChangeType != WatcherChangeTypes.Changed)
                return;

            Log.Warn($"File {e.FullPath} changed!");

        }

        private void MarketWatcher_Deleted(object sender, FileSystemEventArgs e)
        {
            //Market offer deleted

            //Log.Warn($"File {e.FullPath} deleted! {e.Name}");

            if (MarketOffers.TryRemove(e.Name, out _))
                Log.Error("Removed this file from the dictionary!");


            //Send new offer update to all clients
            Communication.UpdateAllOffers();

        }

        private void MarketWatcher_Created(object sender, FileSystemEventArgs e)
        {
            //New market offer created
            //Log.Warn($"File {e.FullPath} created! {e.Name}");






            if (!TryGetReadMarketFile(e.FullPath, out MarketListing Offer))
                return;




            if (MarketOffers.TryAdd(e.Name, Offer))
                Log.Error("Added this file to the dictionary!");


            //Send new offer update to all clients
            Communication.UpdateAllOffers();
        }






        private static bool TryGetReadMarketFile(string FilePath, out MarketListing Listing)
        {
            //Reads market file from path

            Listing = null;
            try
            {

                using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var sr = new StreamReader(fs, Encoding.UTF8);

                string Data = sr.ReadToEnd();


            
                Listing = JsonConvert.DeserializeObject<MarketListing>(File.ReadAllText(FilePath));
                return true;

            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }




        public static void RemoveMarketListing(ulong Owner, string Name)
        {
            string FileName = GetNameFormat(Owner, Name);

            File.Delete(Path.Combine(MarketFolderDir, FileName));
        }


        public static bool SaveNewMarketFile(MarketListing NewListing)
        {
            //Saves a new market listing

            string FileName = GetNameFormat(NewListing.SteamID, NewListing.Name);

            try
            {
                //Save new market offer
                File.WriteAllText(Path.Combine(MarketFolderDir, FileName), JsonConvert.SerializeObject(NewListing, Formatting.Indented));
                return true;

            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return false;
            }
        }


        public static void SetGridPreview(long EntityID, ulong Owner, string GridName)
        {
            string FileName = GetNameFormat(Owner, GridName);
            if (MarketOffers.TryGetValue(FileName, out MarketListing Offer))
            {
                if (!File.Exists(Path.Combine(MarketFolderDir, FileName)))
                {
                    //Someone this happened?
                    MarketOffers.TryRemove(FileName, out _);
                    return;
                }



                string FolderPath = Path.Combine(Hangar.Config.FolderDirectory, Owner.ToString());
                string GridPath =  Path.Combine(FolderPath, GridName + ".sbc");

                Log.Warn("Loading Grid");
                if (!GridSerializer.LoadGrid(GridPath, out IEnumerable<MyObjectBuilder_CubeGrid> GridBuilders))
                {
                    RemoveMarketListing(Owner, GridName);
                    return;
                }


                //Now attempt to load grid
                
                if(MyEntities.TryGetEntityById(EntityID, out MyEntity entity))
                {
                    MyProjectorBase proj = entity as MyProjectorBase;
                    if(proj != null)
                    {
                        Log.Warn("Setting projection!");

                        proj.SendRemoveProjection();

                        var Grids = GridBuilders.ToList();




                        SendNewProjection.Invoke(proj, new object[] { Grids });
                        proj.AlignToRepairProjector(Grids[0]);

                        //proj.SendNewOffset(new Vector3I(boundingBox.Extents), Vector3I.Zero, .25f, false);
                    }
                }
            }
        }

      


        private static string GetNameFormat(ulong Onwer, string GridName)
        {
            return Onwer + "-" + GridName + ".json";
        }
    }

    public static class OrientationAlgebra
    {
        private static readonly bool[] ValidOrientations =
        {
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            false,
            true,
            true,
            true,
            true,
            true,
            true,
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            false,
            true,
            true,
            true,
            true,
            true,
            true,
            false,
            false,
            true,
            true,
            true,
            true,
            false,
            false,
        };

        private static readonly Vector3I[] ProjectionRotations =
        {
            Vector3I.Zero,
            Vector3I.Zero,
            new Vector3I(-2, -2, -1),
            new Vector3I(-2, -2, 1),
            new Vector3I(-2, -2, -2),
            new Vector3I(-2, -2, 0),
            Vector3I.Zero,
            Vector3I.Zero,
            new Vector3I(-2, 0, -1),
            new Vector3I(-2, 0, 1),
            new Vector3I(-2, 0, 0),
            new Vector3I(-2, 0, -2),
            new Vector3I(-1, -2, 1),
            new Vector3I(-1, -2, -1),
            Vector3I.Zero,
            Vector3I.Zero,
            new Vector3I(-1, -2, -2),
            new Vector3I(-1, -2, 0),
            new Vector3I(-1, 0, 1),
            new Vector3I(-1, 0, -1),
            Vector3I.Zero,
            Vector3I.Zero,
            new Vector3I(-1, 0, 0),
            new Vector3I(-1, 0, -2),
            new Vector3I(-2, 1, 0),
            new Vector3I(-2, 1, -2),
            new Vector3I(-2, 1, -1),
            new Vector3I(-2, 1, 1),
            Vector3I.Zero,
            Vector3I.Zero,
            new Vector3I(-2, -1, -2),
            new Vector3I(-2, -1, 0),
            new Vector3I(-2, -1, -1),
            new Vector3I(-2, -1, 1),
            Vector3I.Zero,
            Vector3I.Zero,
        };

        public static bool ProjectionRotationFromForwardAndUp(Base6Directions.Direction forward, Base6Directions.Direction up, out Vector3I rotation)
        {
            var index = ((int)forward * 6 + (int)up) % 36;
            if (!ValidOrientations[index])
            {
                rotation = Vector3I.Zero;
                return false;
            }

            rotation = ProjectionRotations[index];
            return true;
        }
    }

    public static class GridExtensions
    {
        public static bool AlignToRepairProjector(this MyProjectorBase projector, MyObjectBuilder_CubeGrid gridBuilder)
        {


            projector.Orientation.GetQuaternion(out var gridToProjectorQuaternion);
            var projectorToGridQuaternion = Quaternion.Inverse(gridToProjectorQuaternion);
            if (!OrientationAlgebra.ProjectionRotationFromForwardAndUp(Base6Directions.GetDirection(projectorToGridQuaternion.Forward), Base6Directions.GetDirection(projectorToGridQuaternion.Up), out var projectionRotation))
                return false;

            var anchorBlock = projector.CubeGrid.CubeBlocks.FirstOrDefault();
            if (anchorBlock == null)
                return false;

            var offsetInsideGrid = projector.Position - anchorBlock.Position;
            var projectionOffset = new Vector3I(Vector3.Round(projectorToGridQuaternion * offsetInsideGrid));
            projectionOffset = Vector3I.Clamp(projectionOffset, new Vector3I(-50), new Vector3I(50));

            var p = (IMyProjector)projector;
            p.SetValueBool("KeepProjection", true);
            p.ProjectionOffset = projectionOffset;
            p.ProjectionRotation = projectionRotation;
            p.UpdateOffsetAndRotation();

            return true;
        }
    }

}
