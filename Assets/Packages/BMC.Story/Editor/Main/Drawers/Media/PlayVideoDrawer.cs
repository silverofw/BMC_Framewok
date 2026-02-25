using UnityEditor;
using UnityEngine;

namespace BMC.Story.Editor
{
    public class PlayVideoDrawer : StoryActionDrawer
    {
        public override string MenuPath => "Media/Play Video (播放影片)";
        public override StoryEvent.ActionOneofCase ActionCase => StoryEvent.ActionOneofCase.PlayVideo;
        public override StoryEvent CreateNewEvent() => new StoryEvent { PlayVideo = new PlayVideoAction() };

        public override bool Draw(StoryNode node, StoryEvent evt, StoryLineEditorWindow window)
        {
            bool changed = false;
            EditorGUI.BeginChangeCheck();
            evt.PlayVideo.VideoPath = EditorGUILayout.TextField("Video Path", evt.PlayVideo.VideoPath);
            evt.PlayVideo.Volume = EditorGUILayout.Slider("Volume", evt.PlayVideo.Volume, 0, 1);
            evt.PlayVideo.IsLoop = EditorGUILayout.Toggle("Loop", evt.PlayVideo.IsLoop);
            if (EditorGUI.EndChangeCheck()) changed = true;
            return changed;
        }
    }
}