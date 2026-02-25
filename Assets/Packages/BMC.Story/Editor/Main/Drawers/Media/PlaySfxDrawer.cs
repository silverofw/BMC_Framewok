using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class PlaySfxDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Media/Play SFX (播放音效)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlaySfx;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlaySfx = new PlaySoundEffectAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.PlaySfx.AudioPath = EditorGUILayout.TextField("SFX Path", evt.PlaySfx.AudioPath);
            evt.PlaySfx.Volume = EditorGUILayout.Slider("Volume", evt.PlaySfx.Volume, 0, 1);
            evt.PlaySfx.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlaySfx.IsLoop);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}