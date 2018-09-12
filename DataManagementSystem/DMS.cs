using System;

namespace DataManagementSystem
{
    public class DMS
    {
        public DMS()
        {
            
        }

        public static string GetInfo()
        {
            return "This library was created by adan2013. More info and full documentation you can find here: https://github.com/adan2013";
        }

        #region "SAVING"

        public void SaveAs()
        {
            //TODO save as
        }

        public void SaveChanges()
        {
            //TODO save changes
        }

        #endregion

        #region "UNDOREDO"

        public void AddCheckPoint(string info = "")
        {
            //TODO add checkpoint
        }

        public void UndoChanges()
        {
            //TODO undo
        }

        public void RedoChanges()
        {
            //TODO redo
        }

        public void DeleteHistoryOfChanges()
        {
            //TODO delete history of changes
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
    }
}
