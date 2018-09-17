using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

namespace DataManagementSystem
{
    public class DMS<T> where T : class
    {
        //CONST
        const string DebugPrefix = "DMS: ";
        const string UndoRedoFilePrefix = "UR-DMS";
        const string AutoSaveFilePrefix = "AS-DMS";
        const string TempFileExtension = ".dms";
        const int DefaultLimitUndoRedo = 8;

        //EVENTS
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public event FileSavedDelegate FileSaved;
        public delegate void FileUpdatedDelegate(ref T obj);
        public event FileUpdatedDelegate FileUpdated;

        //READONLY PROPERTIES
        public string UID { get; private set; }
        public WorkingState Mode { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }
        public bool UndoRedoActivated { get; private set; }
        public bool UndoAvailable
        {
            get
            {
                if (UndoHistory.Count > 0 & UndoRedoActivated) { return true; } else { return false; }
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
                    DebugLog("Correct value of Undo/Redo limit: 1-10, Default: " + DefaultLimitUndoRedo);
                }
            }
        }
        
        //VARIABLES
        T DataObject;
        bool DebugMode = true;
        Stack<HistoryUndoRedo> UndoHistory = new Stack<HistoryUndoRedo>();
        HistoryUndoRedo CurrentCP = new HistoryUndoRedo("", "");
        Stack<HistoryUndoRedo> RedoHistory = new Stack<HistoryUndoRedo>();
        int pLimitUndoRedo = DefaultLimitUndoRedo;
        int pStackUndoRedoCursor = 0;

        //ENUMS
        public enum WorkingState
        {
            StaticFile = 0,
            ProjectFile = 1
        }

        //STRUCT
        private struct HistoryUndoRedo
        {
            public readonly string message;
            public readonly string filename;

            public HistoryUndoRedo(string message, string filename)
            {
                this.message = message;
                this.filename = filename;
            }
        }

        public DMS(string id, ref T obj, WorkingState mode, string file)
        {
            if (!ValidateID(id))
            {
                throw new Exception("Incorrect ID value! Check documentation for more information!");
            }
            UID = id.ToUpper();
            DataObject = obj;
            Mode = mode;
            PathToFile = file;
            FileChanged = false;
            DeleteCheckPoints();
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
            return LoadFromFile(PathToFile);
        }

        private bool LoadFromFile(string path)
        {
            if (PathToFile == "" || !File.Exists(PathToFile))
            {
                DebugLog("File \"" + path + "\" not found!");
                return false;
            }
            T o = DeserializeObject(PathToFile);
            if (o == null) { return false; }
            DataObject = o;
            FileChanged = false;
            DebugLog("File \"" + path + "\" has been loaded!");
            FileUpdated?.Invoke(ref DataObject);
            return true;
        }

        #endregion

        #region "SAVING"

        public bool SaveAs(string newpath)
        {
            if (Mode == WorkingState.StaticFile)
            {
                DebugLog("SaveAs it's only available in \"ProjectFile mode\"!");
                return false;
            }
            PathToFile = newpath;
            FileChanged = true;
            return SaveChanges();
        }

        public bool SaveChanges()
        {
            if (!FileChanged)
            {
                DebugLog("No changes detected!");
                return false;
            }
            bool r = SerializeObject(ref DataObject, PathToFile);
            if (r)
            {
                FileChanged = false;
                DebugLog("File \"" + PathToFile + "\" has been saved!");
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

        public void ActivateUndoRedo(int limit)
        {
            DeleteUndoHistory();
            DeleteRedoHistory();
            UndoRedoActivated = true;
            pLimitUndoRedo = limit;
            DeleteCheckPoints();
        }

        public void DisableUndoRedo()
        {
            ActivateUndoRedo(DefaultLimitUndoRedo);
            UndoRedoActivated = false;
        }

        public bool AddCheckPoint(string info = "")
        {
            if (!UndoRedoActivated) { return false; }
            FileChanged = true;
            HistoryUndoRedo f = new HistoryUndoRedo(info, CreateCPFile());
            if (f.filename == "") { return false; }
            DeleteRedoHistory();
            UndoHistory.Push(f);
            CurrentCP = new HistoryUndoRedo("", "");
            DebugLog("New Check Point! File: \"" + f.filename + "\", Message: \"" + f.message + "\"");
            return true;
        }

        public void Undo()
        {
            if (!UndoRedoActivated | !UndoAvailable) { return; }
            FileChanged = true;

            if (CurrentCP.filename == "")
            {
                //save object in redo and load undo cp
                HistoryUndoRedo f = new HistoryUndoRedo("", CreateCPFile());
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
                DebugLog("Undo operation completed!");
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
                DebugLog("Redo operation completed!");
            }
        }

        public List<string> GetUndoHistory()
        {
            List<string> r = new List<string>();
            foreach(HistoryUndoRedo i in UndoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        public List<string> GetRedoHistory()
        {
            List<string> r = new List<string>();
            foreach (HistoryUndoRedo i in RedoHistory)
            {
                r.Add(i.message);
            }
            return r;
        }

        private string CreateCPFile()
        {
            string guid = Guid.NewGuid().ToString();
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates) + "\\";
            string f = UndoRedoFilePrefix + "-" + UID + "-" + guid + TempFileExtension;
            bool r = SerializeObject(ref DataObject, d + f);
            if (r) { return f; } else { return ""; }
        }

        private void DeleteCheckPoints()
        {
            string d = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
            string f = UndoRedoFilePrefix + "-" + UID + "*" + TempFileExtension;
            foreach (string i in Directory.GetFiles(d, f))
            {
                try
                {
                    File.Delete(i);
                    DebugLog("Check Point deleted! File: \"" + new FileInfo(i).Name + "\"");
                } catch { }
            }
        }

        private void DeleteUndoHistory()
        {
            UndoHistory.Clear();
        }

        private void DeleteRedoHistory()
        {
            RedoHistory.Clear();
        }

        #endregion

        #region "AUTOSAVE"

        public void CreateAutosave()
        {
            //TODO create autosave
        }

        public void CancelAutosave()
        {
            //TODO cancel autosave
        }

        public int GetTimeLastAutosave()
        {
            //TODO last time autosave
            return 0;
        }

        #endregion

        #region "SERIALIZATION"

        private bool SerializeObject(ref T obj, string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(path, FileMode.Create, FileAccess.Write);
                formatter.Serialize(stream, obj);
                stream.Close();
                DebugLog("Serialization successful! File: \"" + path + "\"");
                return true;
            }
            catch {
                DebugLog("Serialization error! File: \"" + path + "\"");
                return false;
            }
        }

        private T DeserializeObject(string path)
        {
            try
            {
                IFormatter formatter = new BinaryFormatter();
                Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read);
                T o = (T)formatter.Deserialize(stream);
                DebugLog("Deserialization successful! File: \"" + path + "\"");
                return o;
            }
            catch {
                DebugLog("Deserialization error! File: \"" + path + "\"");
                return null;
            }
        }

        #endregion

        #region "DEBUG"

        public void TurnOnDebugMode()
        {
            DebugMode = true;
        }

        public void TurnOffDebugMode()
        {
            DebugMode = false;
        }

        private void DebugLog(string message)
        {
            if (DebugMode) { System.Diagnostics.Debug.WriteLine(DebugPrefix + message); }
        }

        #endregion

    }
}
