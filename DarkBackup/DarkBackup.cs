﻿using System;
using System.Collections.Generic;
using System.IO;
using DarkMultiPlayerServer;
namespace LunaBackup
{
    public class DarkBackup : DMPPlugin
    {
        //Set backup interval in ticks
        private const long BACKUP_INTERVAL = 60 * TimeSpan.TicksPerSecond;
        //State stuff
        private BackupCommon.BackupCommon backupCommon;
        private bool started = false;
        private long nextBackupTime = 0;

        public override void OnServerStart()
        {
            string backupPath = Path.Combine(Server.universeDirectory, "DarkBackup");
            string restorePath = Server.universeDirectory;
            List<string> safeDeleteList = new List<string>();
            safeDeleteList.Add("Crafts");
            safeDeleteList.Add("Flags");
            safeDeleteList.Add("Kerbals");
            safeDeleteList.Add("Scenarios");
            safeDeleteList.Add("Screenshots");
            safeDeleteList.Add("Players");
            safeDeleteList.Add("Vessels");
            safeDeleteList.Add("subspace.txt");
            List<string> ignoreList = new List<string>();
            ignoreList.Add("DarkBackup");
            ignoreList.Add("Screenshots");
            backupCommon = new BackupCommon.BackupCommon(backupPath, restorePath, DarkLog.Debug, DarkLog.Normal, DarkLog.Error, Shutdown, safeDeleteList, ignoreList);
            CommandHandler.RegisterCommand("restore", backupCommon.RestoreCommand, "Restores the universe to the time or backup specified");
            started = true;
        }

        private void Shutdown()
        {
            Server.ShutDown("Shutting down to restore universe");
        }

        public override void OnServerStop()
        {
            if (backupCommon.restoreID != 0)
            {
                DateTime restoreDateTime = new DateTime(backupCommon.restoreID, DateTimeKind.Utc);
                DarkLog.Normal("Restoring backup from: " + restoreDateTime.ToLocalTime().ToLongDateString() + " " + restoreDateTime.ToLocalTime().ToLongTimeString());
                backupCommon.RestoreUniverse(backupCommon.restoreID);
            }
            backupCommon.restoreID = 0;
        }

        public override void OnUpdate()
        {
            if (!started)
            {
                return;
            }
            long backupID = DateTime.UtcNow.Ticks;
            if (backupID > nextBackupTime)
            {
                nextBackupTime = backupID + BACKUP_INTERVAL;
                backupCommon.BackupUniverse(backupID);
            }
        }
    }
}
