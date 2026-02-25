using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class PlayBgmDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Media/Play BGM (播放背景音樂)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlayBgm;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlayBgm = new PlayBackgroundMusicAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.PlayBgm.AudioPath = EditorGUILayout.TextField("BGM Path", evt.PlayBgm.AudioPath);
            evt.PlayBgm.Volume = EditorGUILayout.Slider("Volume", evt.PlayBgm.Volume, 0, 1);
            evt.PlayBgm.FadeInDuration = EditorGUILayout.FloatField("Fade In (sec)", evt.PlayBgm.FadeInDuration);
            evt.PlayBgm.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlayBgm.IsLoop);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}