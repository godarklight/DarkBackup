using System;
using System.Collections.Generic;
using System.IO;
using Server;
using Server.Command;
using Server.Context;
using Server.Log;
using Server.Plugin;
namespace LunaBackup
{
    public class LunaBackup : LmpPlugin
    {
        //Set backup interval in ticks
        private const long BACKUP_INTERVAL = 60 * TimeSpan.TicksPerSecond;
        //State stuff
        private BackupCommon.BackupCommon backupCommon;
        private bool started = false;
        private long nextBackupTime = 0;

        public override void OnServerStart()
        {
            string backupPath = Path.Combine(ServerContext.UniverseDirectory, "LunaBackup");
            string restorePath = ServerContext.UniverseDirectory;
            List<string> safeDeleteList = new List<string>();
            safeDeleteList.Add("Crafts");
            safeDeleteList.Add("Flags");
            safeDeleteList.Add("Groups");
            safeDeleteList.Add("Kerbals");
            safeDeleteList.Add("Scenarios");
            safeDeleteList.Add("Screenshots");
            safeDeleteList.Add("Vessels");
            safeDeleteList.Add("StartTime.txt");
            safeDeleteList.Add("Subspace.txt");
            List<string> ignoreList = new List<string>();
            ignoreList.Add("LunaBackup");
            ignoreList.Add("Screenshots");
            backupCommon = new BackupCommon.BackupCommon(backupPath, restorePath, LunaLog.Debug, LunaLog.Normal, LunaLog.Error, Shutdown, safeDeleteList, ignoreList);
            CommandDefinition cd = new CommandDefinition("restore", RestoreCommand, "Restores the universe to the time or backup specified");
            CommandHandler.Commands.TryAdd("restore", cd);
            started = true;
        }

        private bool RestoreCommand(string commandText)
        {
            backupCommon.RestoreCommand(commandText);
            return true;
        }

        private void Shutdown()
        {
            Server.MainServer.Restart();
        }

        public override void OnServerStop()
        {
            if (backupCommon.restoreID != 0)
            {
                DateTime restoreDateTime = new DateTime(backupCommon.restoreID, DateTimeKind.Utc);
                LunaLog.Normal("Restoring backup from: " + restoreDateTime.ToLocalTime().ToLongDateString() + " " + restoreDateTime.ToLocalTime().ToLongTimeString());
                LunaLog.Normal("Waiting 10 seconds for the server to shut down");
                System.Threading.Thread.Sleep(10000);
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
