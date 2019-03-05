using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DarkMultiPlayerServer;
namespace DarkBackup
{
    public class DarkBackup : DMPPlugin
    {
        //Set backup interval in ticks
        private const long BACKUP_INTERVAL = 60 * TimeSpan.TicksPerSecond;
        //State stuff
        private string darkBackupPath;
        private SHA256Managed sha256 = new SHA256Managed();
        private long nextBackupTime = 0;
        private long restoreID = 0;
        private bool started = false;
        private List<string> safeDeleteList = new List<string>();
        public override void OnServerStart()
        {
            CommandHandler.RegisterCommand("restore", RestoreCommand, "Restores the universe to the time or backup specified");
            darkBackupPath = Path.Combine(Server.universeDirectory, "DarkBackup");
            Directory.CreateDirectory(darkBackupPath);
            Directory.CreateDirectory(Path.Combine(darkBackupPath, "Objects"));
            Directory.CreateDirectory(Path.Combine(darkBackupPath, "Index"));
            safeDeleteList.Add("Crafts");
            safeDeleteList.Add("Flags");
            safeDeleteList.Add("Kerbals");
            safeDeleteList.Add("Scenarios");
            safeDeleteList.Add("Screenshots");
            safeDeleteList.Add("Players");
            safeDeleteList.Add("Vessels");
            safeDeleteList.Add("subspace.txt");
            started = true;
        }

        public override void OnServerStop()
        {
            if (restoreID != 0)
            {
                DateTime restoreDateTime = new DateTime(restoreID, DateTimeKind.Local);
                DarkLog.Normal("Restoring backup from: " + restoreDateTime.ToLongDateString() + " " + restoreDateTime.ToLongTimeString());
                RestoreUniverse(restoreID);
            }
            restoreID = 0;
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
                BackupUniverse(backupID);
            }
        }

        private void BackupUniverse(long backupID)
        {
            int totalFilesBackedUp = 0;
            int newFilesBackedUp = 0;
            string[] universeFiles = Directory.GetFiles(Server.universeDirectory, "*", SearchOption.AllDirectories);
            Dictionary<string, string> backupFiles = new Dictionary<string, string>();
            foreach (string universeFile in universeFiles)
            {
                string cropUniverseFile = universeFile.Substring(Server.universeDirectory.Length + 1);
                if (cropUniverseFile.StartsWith("DarkBackup", StringComparison.InvariantCultureIgnoreCase))
                {
                    continue;
                }
                byte[] fileBytes = File.ReadAllBytes(universeFile);
                string fileSha = GetSHA256Hash(fileBytes);
                string backupName = Path.Combine(darkBackupPath, "Objects", fileSha + ".txt");
                backupFiles.Add(cropUniverseFile, fileSha);
                totalFilesBackedUp++;
                if (!File.Exists(backupName))
                {
                    File.Copy(universeFile, backupName);
                    newFilesBackedUp++;
                    //DarkLog.Debug("Backing up: " + cropUniverseFile);
                }
            }
            using (StreamWriter sw = new StreamWriter(Path.Combine(darkBackupPath, "Index", backupID + ".txt")))
            {
                foreach (KeyValuePair<string, string> kvp in backupFiles)
                {
                    sw.WriteLine(kvp.Key + ":" + kvp.Value);
                }
            }
            DarkLog.Debug("Plugin backing up universe, new: " + newFilesBackedUp + ", total: " + totalFilesBackedUp + ", id: " + backupID);
        }

        private void RestoreCommand(string backupText)
        {
            if (backupText == "")
            {
                DarkLog.Normal("There is 3 ways to select the backup");
                DarkLog.Normal("1) The exact backup given in Universe/DarkBackup/Index");
                DarkLog.Normal("2) The closest backup given to a date in the format yyyy-mm-dd HH-MM-SS: 2019-3-14 1:59PM (use capital AM or PM)");
                DarkLog.Normal("2) The closest backup given to a relative time ago (xd xh xm xs): 1d 2h");
            }
            if (long.TryParse(backupText, out long backupID))
            {
                restoreID = backupID;
            }
            else
            {


                if (backupText.Contains("d") || backupText.Contains("h") || backupText.Contains("m") || backupText.Contains("s"))
                {
                    DarkLog.Normal("Relative parsed!");
                    DateTime relativeTime = DateTime.UtcNow;
                    string[] relativeSplit = backupText.Split(' ');
                    foreach (string relativeSplitPart in relativeSplit)
                    {
                        if (relativeSplitPart.Contains("d"))
                        {
                            string numberPart = relativeSplitPart.Replace("d", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddDays(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("h"))
                        {
                            string numberPart = relativeSplitPart.Replace("h", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddHours(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("m"))
                        {
                            string numberPart = relativeSplitPart.Replace("m", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddMinutes(-numberPartDouble);
                            }
                        }
                        if (relativeSplitPart.Contains("s"))
                        {
                            string numberPart = relativeSplitPart.Replace("s", "");
                            if (double.TryParse(numberPart, out double numberPartDouble))
                            {
                                relativeTime = relativeTime.AddSeconds(-numberPartDouble);
                            }
                        }
                    }
                    restoreID = GetClosestBackup(relativeTime);
                }
                else
                {
                    if (DateTime.TryParse(backupText, out DateTime targetDateTime))
                    {
                        restoreID = GetClosestBackup(targetDateTime);
                    }
                }
            }
            if (restoreID != 0)
            {
                Server.ShutDown("Shutting down to restore universe");
            }
            else
            {
                DarkLog.Normal("No backup found");
            }
        }

        private long GetClosestBackup(DateTime targetDateTime)
        {
            long targetTicks = targetDateTime.Ticks;
            string[] possibleBackups = Directory.GetFiles(Path.Combine(darkBackupPath, "Index"));
            long closestBackup = 0;
            long closestBackupDiff = long.MaxValue;
            foreach (string possibleBackup in possibleBackups)
            {
                string possibleBackupCropped = Path.GetFileNameWithoutExtension(possibleBackup);
                if (long.TryParse(possibleBackupCropped, out long possibleBackupLong))
                {
                    long timeDiff = Math.Abs(targetTicks - possibleBackupLong);
                    if (timeDiff < closestBackupDiff)
                    {
                        closestBackup = possibleBackupLong;
                        closestBackupDiff = timeDiff;
                    }
                }
            }
            return closestBackup;
        }

        private void RestoreUniverse(long backupID)
        {
            if (!File.Exists(Path.Combine(darkBackupPath, "Index", backupID + ".txt")))
            {
                DarkLog.Normal("Cannot restore to time that does not exist!");
                return;
            }
            else
            {
                long beforeRestoreID = DateTime.UtcNow.Ticks;
                DarkLog.Debug("To restore the universe before this backup, use ID: " + beforeRestoreID);
                BackupUniverse(beforeRestoreID);
                string[] oldFiles = Directory.GetFiles(Server.universeDirectory, "*", SearchOption.AllDirectories);
                string[] newFiles = File.ReadAllLines(Path.Combine(darkBackupPath, "Index", backupID + ".txt"));
                foreach (string oldFile in oldFiles)
                {
                    string cropOldFile = oldFile.Substring(Server.universeDirectory.Length + 1);
                    foreach (string safeStartText in safeDeleteList)
                    {
                        if (cropOldFile.StartsWith(safeStartText, StringComparison.InvariantCultureIgnoreCase))
                        {
                            DarkLog.Debug("Deleting: " + cropOldFile);
                            File.Delete(oldFile);
                            continue;
                        }
                    }
                }
                foreach (string unsplitRestoreLine in newFiles)
                {
                    int splitIndex = unsplitRestoreLine.LastIndexOf(":", StringComparison.InvariantCultureIgnoreCase);
                    string pathPart = unsplitRestoreLine.Substring(0, splitIndex);
                    string hash = unsplitRestoreLine.Substring(splitIndex + 1);
                    DarkLog.Debug("Restoring: " + pathPart + " from " + hash + ".txt");
                    string backupLocation = Path.Combine(darkBackupPath, "Objects", hash + ".txt");
                    string realLocation = Path.Combine(Server.universeDirectory, pathPart);
                    if (File.Exists(backupLocation))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(realLocation));
                        File.Copy(backupLocation, realLocation);
                    }
                    else
                    {
                        DarkLog.Error("Object " + hash + ".txt is missing for " + realLocation);
                    }
                }
            }
        }

        private string GetSHA256Hash(byte[] input)
        {

            byte[] hash = sha256.ComputeHash(input);
            string hashString = string.Empty;
            foreach (byte x in hash)
            {
                hashString += String.Format("{0:x2}", x);
            }
            return hashString;
        }
    }
}
