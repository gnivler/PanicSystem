﻿using System.Collections;

 // thank you Frosty IRBTModUtils CustomDialog
 // https://github.com/IceRaptor/IRBTModUtils
namespace IRBTModUtils {
    public static class ModState {

        public static Queue DialogueQueue = new Queue();
        public static bool IsDialogStackActive = false;

        public static void Reset() {
            // Reinitialize state
            DialogueQueue.Clear();
            IsDialogStackActive = false;
        }
    }

}
