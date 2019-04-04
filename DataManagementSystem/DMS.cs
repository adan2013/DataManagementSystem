using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Timers;

namespace DataManagementSystem
{
    public class DMS<T> where T : class
    {
        //CONST
        const string DEBUG_PREFIX = "DMS: ";
        const string UR_PREFIX = "UR-DMS-";
        const string AS_PREFIX = "AS-DMS-";
        const string AS_INFO_PREFIX = "AS-INFO-DMS-";
        const string BU_PREFIX = "BU-DMS-";
        const string BU_DATABASE_PREFIX = "BU-DATABASE-DMS-";

        const string TEMP_FILE_EXT = ".dms";
        const string INFO_FILE_EXT = ".dmsinfo";
        const string DATABASE_FILE_EXT = ".dmsdatabase";

        const int DEFAULT_UR_LIMIT = 8;

        //EVENTS
        public event FileSavedDelegate FileSaved;
        public event FileUpdatedDelegate FileUpdated;

        public event AutoSaveCreatedDelegate AutoSaveCreated;
        public event AutoSaveDetectedDelegate AutoSaveDetected;

        //DELEGATES
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public delegate void FileUpdatedDelegate(ref T obj);

        public delegate void AutoSaveCreatedDelegate(ref T obj);
        public delegate void AutoSaveDetectedDelegate(DateTime time, string project, string autosave);

        //READONLY PROPERTIES
        public string UID { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }

        public bool UndoRedoActivated { get; private set; }
        public bool AutosaveActivated { get; private set; }
        public bool BackupActivated { get; private set; }

        public bool UndoAvailable
        {
            get
            {
                if (UndoHistory.Count > 0 & UndoRedoActivated)
                {
                    if (RedoHistory.Count >= pLimitUndoRedo)
                    {
                        DebugMsg("You reached the limit of undo operations!");
                        return false;
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            set { }
        }
        public bool RedoAvailable
        {
            get
            {
                if (RedoHistory.Count > 0 & UndoRedoActivated) { return true; } else { return false; }
            }
            set { }
        }

        public bool RestoreMode { get; private set; }

        //PROPERTIES
        public int LimitUndoRedo
        {
            get
            {
                return pLimitUndoRedo;
            }
            private set
            {
                if (value > 0 & value < 11)
                {
                    pLimitUndoRedo = value;
                }
                else
                {
                    DebugMsg("Correct value of Undo/Redo limit: 1-10, Default: " + DEFAULT_UR_LIMIT);
                    pLimitUndoRedo = DEFAULT_UR_LIMIT;
                }
            }
        }
        
        //VARIABLES
        T DataObject;

        Stack<UndoRedoInfo> UndoHistory = new Stack<UndoRedoInfo>();
        UndoRedoInfo CurrentCP = new UndoRedoInfo("", "");
        Stack<UndoRedoInfo> RedoHistory = new Stack<UndoRedoInfo>();

        int pLimitUndoRedo = DEFAULT_UR_LIMIT;
        Timer AutosaveTMR = new Timer();
        string backupWarehouse = "";
        DMSBackupDatabase backupDB = new DMSBackupDatabase();

        //STRUCT
        private struct UndoRedoInfo
        {
            public readonly string message;
            public readonly string filename;

            public UndoRedoInfo(string message, string filename)
            {
                this.message = message;
                this.filename = filename;
            }
        }
        
        public DMS(string id, ref T obj, string file)
        {
            if (!ValidateID(id))
            {
                throw new Exception("Incorrect ID value! Check documentation for more information!");
            }
            UID = id.ToUpper();
            T t = obj ?? throw new Exception("Data object is empty!");
            DataObject = t;
            PathToFile = file;
            FileChanged = false;
            //UNDOREDO
            DeleteCheckPoints();
            //AUTOSAVE
            AutosaveTMR.Elapsed += TimerTick;
        }

        private bool ValidateID(string input)
        {
            if (input.Length == 0 | input.Length > 10) { return false; }
            input = input.ToUpper();
            for (int i = 0; i < input.Length; i++)
            {
                if ("1234567890QWERTYUIOPASDFGHJKLZXCVBNM".IndexOf(input.Substring(i, 1)) < 0) { return false; }
            }
            return true;
        }

        public static string GetInfo()
        {
            return "This library was created by adan2013. More info and full documentation you can find here: https://github.com/adan2013/DataManagementSystem";
        }

        #region "RECOVERY"

        public bool LoadFromSource()
        {
            if (PathToFile == "")
            {
                DebugMsg("Use \"SaveAs\" to define the save path!");
                return false;
            }
            return LoadFromFile(PathToFile, true);
        }

        private bool LoadFromFile(string path, bool originalfile = false)
        {
            if (path == "" || !File.Exists(path))
            {
                DebugMsg("File \"" + path + "\" not found!");
                return false;
            }
            T o = DeserializeObject(path);
            if (o == null) { return false; }
            DataObject = o;
            if (originalfile) { FileChanged = false; }
            DebugMsg("File \"" + path + "\" has been loaded!");
            FileUpdated?.Invoke(ref DataObject);
            return true;
        }

        #endregion

        #region "SAVING"

        public bool SaveAs(string newpath)
        {
            if (newpath == "")
            {
                DebugMsg("New path is empty!");
                return false;
            }
            PathToFile = newpath;
            FileChanged = true;
            return SaveChanges();
        }

        public bool SaveChanges()
        {
            if (PathToFile == "")
            {
                DebugMsg("Use \"SaveAs\" to define the save path!");
                return false;
            }
            if (!FileChanged)
            {
                DebugMsg("No changes detected!");
                return false;
            }
            bool r = SerializeObject(ref DataObject, PathToFile);
            if (r)
            {
                FileChanged = false;
                DebugMsg("File \"" + PathToFile + "\" has been saved!");
                FileSaved?.Invoke(this, PathToFile);
                return true;
            }
            return false;
        }

        public void CheckChanges()
        {
            FileChanged = true;
        }

        #endregion

        #region "UNDOREDO"

        public void UndoRedoService(bool activation, int limit)
        {
            UndoRedoActivated = activation;
            DeleteUndoHistory();
            DeleteRedoHistory();
            pLimitUndoRedo = limit;
            DeleteCheckPoints();
        }

        public bool AddCheckPoint(string info = "")
        {
            if (!UndoRedoActivated) { return false; }
            FileChanged = true;
            UndoRedoInfo f = new UndoRedoInfo(info, CreateCPFile());
            if (f.filename == "") { return false; }
            DeleteRedoHistory();
            UndoHistory.Push(f);
            CurrentCP = new UndoRedoInfo("", "");
            DebugMsg("New Check Point! File: \"" + f.filename + "\", Message: \"" + f.message + "\"");
            return true;
        }

        public void Undo()
        {
            if (!UndoRedoActivated | !UndoAvailable) { return; }
            FileChanged = true;

            if (CurrentCP.filename == "")
            {
                //save object in redo and load undo cp
                UndoRedoInfo f = new UndoRedoInfo("", CreateCPFile());
                if (f.filename == "") { return; }
                RedoHistory.Push(f);
            }
            else
            {
                //save current cp in redo and load undo cp
                RedoHistory.Push(CurrentCP);
            }
            CurrentCP = UndoHistory.Pop();
            if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + CurrentCP.filename))
            {
                DebugMsg("Undo operation completed!");
            }
        }

        public void Redo()
        {
            if (!UndoRedoActivated | !RedoAvailable) { return; }
            FileChanged = true;
 
            UndoHistory.Push(CurrentCP);
            CurrentCP = RedoHistory.Pop();
            if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + CurrentCP.filename))
            {
                DebugMsg("Redo operation completed!");
            }
        }

        public List<string> GetUndoHistory()
        {
            List<string> r = new List<string>();
            foreach(UndoRedoInfo i in UndoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        public List<string> GetRedoHistory()
        {
            List<string> r = new List<string>();
            foreach (UndoRedoInfo i in RedoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        private string CreateCPFile()
        {
            string guid = Guid.NewGuid().ToString();
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = UR_PREFIX + UID + "-" + guid + TEMP_FILE_EXT;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r) { return f; } else { return ""; }
        }

        private void DeleteCheckPoints()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
            string f = UR_PREFIX + UID + "*" + TEMP_FILE_EXT;
            foreach (string i in Directory.GetFiles(d, f))
            {
                try
                {
                    File.Delete(i);
                    DebugMsg("Check Point deleted! File: \"" + new FileInfo(i).Name + "\"");
                } catch { }
            }
        }

        private void DeleteUndoHistory()
        {
            UndoHistory.Clear();
        }

        private void DeleteRedoHistory()
        {
            while (RedoHistory.Count > 0)
            {
                UndoRedoInfo f = RedoHistory.Pop();
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + f.filename);
                    DebugMsg("Check Point deleted! File: \"" + f.filename + "\"");
                } catch { }
            }
        }

        #endregion

        #region "AUTOSAVE"

        public void AutoSaveService(bool activation, int interval)
        {
            if (interval < 1 | interval > 30)
            {
                DebugMsg("Correct value of AutoSave Interval: 1-30. AutoSave disabled!");
                activation = false;
                interval = 30;
            }
            AutosaveActivated = activation;
            AutosaveTMR.Enabled = false;
            AutosaveTMR.Interval = interval * 60000;
            if(activation)
            {
                AutosaveTMR.Enabled = true;
                DebugMsg("AutoSave activated! Interval: " + interval + " minutes");
            }
        }
        
        public bool CreateAutoSaveFile()
        {
            if (RestoreMode)
            {
                DebugMsg("AutoSave is disabled in \"Restore Mode\"!");
                return false;
            }
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = AS_PREFIX + UID + TEMP_FILE_EXT;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r)
            {
                DMSAutosaveInfo i = new DMSAutosaveInfo(DateTime.Now, PathToFile, d + f);
                SerializeInfo(ref i, d + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                DebugMsg("New AutoSave! File: \"" + f + "\"");
                AutoSaveCreated?.Invoke(ref DataObject);
            }
            return r;
        }
        
        public void DeleteRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
            {
                try
                {
                    File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT);
                    if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT))
                    {
                        File.Delete(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                    }
                    RestoreMode = false;
                    DebugMsg("AutoSave file deleted!");
                } catch { }
            }
        }

        public void LoadFromRestoreFile()
        {
            if (RestoreMode)
            {
                try
                {
                    if (LoadFromFile(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
                    {
                        RestoreMode = false;
                        FileChanged = true;
                        PathToFile = "";
                        DebugMsg("The data was restored successfully!");
                        DeleteRestoreFile();
                    }
                }
                catch { }
            }
            else
            {
                DebugMsg("Restore mode disabled!");
            }
        }

        public void CheckRestoreFile()
        {
            if (File.Exists(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_PREFIX + UID + TEMP_FILE_EXT))
            {
                DMSAutosaveInfo i = DeserializeInfo(Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\" + AS_INFO_PREFIX + UID + INFO_FILE_EXT);
                if (i == null) i = new DMSAutosaveInfo(DateTime.Now, "", "");
                RestoreMode = true;
                DebugMsg("Data ready to recover! Use LoadRestoreFile() or DeleteRestoreFile() to choose what you want to do...");
                AutoSaveDetected?.Invoke(i.CreationTime, i.ProjectFile, i.AutoSaveFile);
            }
        }

        private void TimerTick(Object source, ElapsedEventArgs e)
        {
            CreateAutoSaveFile();
        }

        #endregion

        #region "BACKUP"

        public void BackUpService(bool activation, string warehouse)
        {
            if (!Directory.Exists(warehouse))
            {
                DebugMsg("Directory with backups doesn't exist!. BackUp disabled!");
                activation = false;
                warehouse = "";
            }
            BackupActivated = activation;
            backupWarehouse = warehouse;
            if (activation)
            {
                DebugMsg("BackUp activated! Warehouse: \"" + warehouse + "\"");
                LoadBackupDatabase();
            }
        }

        public bool CreateBackUp(string message = "")
        {
            if(!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            string guid = Guid.NewGuid().ToString();
            string d = backupWarehouse + "\\";
            string f = BU_PREFIX + UID + "-" + guid + TEMP_FILE_EXT;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r)
            {
                backupDB.items.Add(new DMSBackupInfo(DateTime.Now, message, d + f));
                SaveBackupDatabase();
                DebugMsg("New Backup! File: \"" + f + "\"");
                return true;
            }
            return false;
        }

        public List<DMSBackupInfo> GetBackUpList()
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return new List<DMSBackupInfo>();
            }
            return backupDB.items;
        }

        public bool DeleteEmptyBackups()
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            List<DMSBackupInfo> empty = new List<DMSBackupInfo>();
            foreach(DMSBackupInfo i in backupDB.items)
            {
                if(!File.Exists(i.File)) { empty.Add(i); }
            }
            if(empty.Count > 0)
            {
                foreach (DMSBackupInfo i in empty) DeleteBackUp(i);
                return true;
            }
            return false;
        }

        public bool LoadFromBackUp(DMSBackupInfo obj, bool replaceFile)
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            try
            {
                if (LoadFromFile(obj.File))
                {
                    FileChanged = true;
                    DebugMsg("The data was restored successfully!");
                    if (replaceFile)
                    {
                        return SaveChanges();
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch { return false; }
        }

        public bool DeleteBackUp(DMSBackupInfo obj)
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            if (obj==null || !File.Exists(obj.File)) { return false;  }
            try
            {
                File.Delete(obj.File);
                backupDB.items.Remove(obj);
                SaveBackupDatabase();
                DebugMsg("BackUp file deleted! Name: " + new FileInfo(obj.File).Name);
                return true;
            } catch { return false; }
        }

        private bool LoadBackupDatabase()
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            DMSBackupDatabase i = DeserializeDatabase(backupWarehouse + "\\" + BU_DATABASE_PREFIX + UID + DATABASE_FILE_EXT);
            if (i == null)
            {
                backupDB = new DMSBackupDatabase();
                DebugMsg("Backup database loading error!");
                return false;
            }
            else
            {
                backupDB = i;
                DebugMsg("Backup database has been loaded!");
                return true;
            }
        }

        private bool SaveBackupDatabase()
        {
            if (!BackupActivated)
            {
                DebugMsg("Backup disabled!");
                return false;
            }
            if (backupWarehouse == "" || !Directory.Exists(backupWarehouse)) { return false; }
            if(backupDB == null) { backupDB = new DMSBackupDatabase(); }
            if(SerializeDatabase(ref backupDB, backupWarehouse + "\\" + BU_DATABASE_PREFIX + UID + DATABASE_FILE_EXT))
            {
                DebugMsg("Backup database saved!");
                return true;
            }
            return false;
        }

        #endregion

        #region "SERIALIZATION"

        private bool SerializeObject(ref T obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    formatter.Serialize(stream, obj);
                    stream.Close();
                }
                DebugMsg("Serialization successful! File: \"" + path + "\"");
                return true;
            }
            catch {
                DebugMsg("Serialization error! File: \"" + path + "\"");
                return false;
            }
        }

        private T DeserializeObject(string path)
        {
            try
            {
                T o;
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    o = (T)formatter.Deserialize(stream);
                }
                DebugMsg("Deserialization successful! File: \"" + path + "\"");
                return o;
            }
            catch {
                DebugMsg("Deserialization error! File: \"" + path + "\"");
                return null;
            }
        }

        private bool SerializeInfo(ref DMSAutosaveInfo obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    formatter.Serialize(stream, obj);
                    stream.Close();
                }
                return true;
            } catch { return false; }
        }

        private DMSAutosaveInfo DeserializeInfo(string path)
        {
            try
            {
                DMSAutosaveInfo o;
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    o = (DMSAutosaveInfo)formatter.Deserialize(stream);
                }
                return o;
            } catch { return null; }
        }

        private bool SerializeDatabase(ref DMSBackupDatabase obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                {
                    formatter.Serialize(stream, obj);
                    stream.Close();
                }
                return true;
            }
            catch { return false; }
        }

        private DMSBackupDatabase DeserializeDatabase(string path)
        {
            try
            {
                DMSBackupDatabase o;
                IFormatter formatter = new BinaryFormatter();
                using (Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    o = (DMSBackupDatabase)formatter.Deserialize(stream);
                }
                return o;
            }
            catch { return null; }
        }

            #endregion

        #region "DEBUG"

            private void DebugMsg(string message)
            {
            #if DEBUG
                System.Diagnostics.Debug.WriteLine(DEBUG_PREFIX + message);
            #endif
            }

        #endregion

        [Serializable()]
        private class DMSAutosaveInfo
        {
            public readonly DateTime CreationTime;
            public readonly string ProjectFile;
            public readonly string AutoSaveFile;

            public DMSAutosaveInfo(DateTime creationTime, string toProject, string toAutoSave)
            {
                CreationTime = creationTime;
                ProjectFile = toProject;
                AutoSaveFile = toAutoSave;
            }
        }

        [Serializable()]
        public class DMSBackupInfo
        {
            public readonly DateTime CreationTime;
            public readonly string Message;
            public readonly string File;

            public DMSBackupInfo(DateTime creationTime, string message, string file)
            {
                CreationTime = creationTime;
                Message = message;
                File = file;
            }
        }

        [Serializable()]
        public class DMSBackupDatabase
        {
            public List<DMSBackupInfo> items;

            public DMSBackupDatabase()
            {
                items = new List<DMSBackupInfo>();
            }
        }

    }
}
