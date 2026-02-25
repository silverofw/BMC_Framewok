using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class PlayVoiceDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Media/Play Voice (播放語音)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlayVoice;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlayVoice = new PlayVoiceAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.PlayVoice.AudioPath = EditorGUILayout.TextField("Voice Path", evt.PlayVoice.AudioPath);
            evt.PlayVoice.Volume = EditorGUILayout.Slider("Volume", evt.PlayVoice.Volume, 0, 1);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}