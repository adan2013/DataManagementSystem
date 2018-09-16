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
        const string debugprefix = "DMS: ";
        const int defaultlimitundoredo = 8;

        //EVENTS
        public delegate void FileSavedDelegate(DMS<T> sender, string path);
        public event FileSavedDelegate FileSaved;
        public delegate void FileUpdatedDelegate(ref T obj);
        public event FileUpdatedDelegate FileUpdated;

        //READONLY PROPERTIES
        public WorkingState Mode { get; private set; }
        public string PathToFile { get; private set; }
        public bool FileChanged { get; private set; }
        public bool UndoRedoActivated { get; private set; }
        public bool UndoAvailable { get; private set; }
        public bool RedoAvailable { get; private set; }
        public int LimitUndoRedo
        {
            get
            {
                return _LimitUndoRedo;
            }
            private set
            {
                if (value > 0 & value < 11)
                {
                    _LimitUndoRedo = value;
                }
                else
                {
                    DebugLog("Correct value of Undo/Redo limit: 1-10, Default: " + defaultlimitundoredo);
                }
            }
        }
        
        //VARIABLES
        T DataObject;
        bool DebugMode = true;
        Stack<HistoryUndoRedo> UndoHistory = new Stack<HistoryUndoRedo>();
        Stack<HistoryUndoRedo> RedoHistory = new Stack<HistoryUndoRedo>();
        int _LimitUndoRedo = defaultlimitundoredo;
        int _StackUndoRedoCursor = 0;

        //ENUMS
        public enum WorkingState
        {
            StaticFile = 0,
            ProjectFile = 1
        }

        //STRUCT
        private struct HistoryUndoRedo
        {
            public string message;
            public string filename;
        }

        public DMS(ref T obj, WorkingState mode, string file)
        {
            DataObject = obj;
            Mode = mode;
            PathToFile = file;
            FileChanged = false;
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
            _LimitUndoRedo = limit;
        }

        public void DisableUndoRedo()
        {
            ActivateUndoRedo(defaultlimitundoredo);
            UndoRedoActivated = false;
        }

        public void AddCheckPoint(string info = "")
        {
            if (!UndoRedoActivated) { return; }
            FileChanged = true;

            //TODO add checkpoint
        }

        public void Undo()
        {
            if (!UndoRedoActivated) { return; }
            //TODO undo
        }

        public void Redo()
        {
            if (!UndoRedoActivated) { return; }
            //TODO redo
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

        private void ManageUndoRedoHistory()
        {
            //TODO magange undo redo history
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
            if (DebugMode) { System.Diagnostics.Debug.WriteLine(debugprefix + message); }
        }

        #endregion

    }
}
