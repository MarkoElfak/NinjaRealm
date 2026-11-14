using NinjaRealm.Core.Content;
using UnityEngine;

namespace NinjaRealm.Presentation
{
    /// <summary>
    /// Put one of these in the first scene. It initializes the content database
    /// before anything else runs and reports content problems to the console.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] ContentDatabase content;

        public static ContentDatabase Content { get; private set; }

        void Awake()
        {
            if (content == null)
            {
                Debug.LogError("[GameBootstrap] No ContentDatabase assigned. Create one with Ninja Realm > Content > Rebuild Database.", this);
                enabled = false;
                return;
            }

            content.Initialize();
            Content = content;

            foreach (var issue in ContentValidation.Validate(content.Definitions))
                Debug.LogWarning(issue.ToString(), issue.Definition);
        }

        void OnDestroy()
        {
            if (Content == content) Content = null;
        }
    }
}
