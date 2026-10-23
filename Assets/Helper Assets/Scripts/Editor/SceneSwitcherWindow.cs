using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SceneSwitcherWindow : EditorWindow
{
    private const string RecentPref = "SceneSwitcher.Recent";
    private const string FavouritesPref = "SceneSwitcher.Favourites";
    private const string SearchControl = "SceneSwitcherSearch";
    private const int MaxRecent = 8;

    private static readonly string[] DefaultFavourites = new string[0];

    private static Texture starTexture;

    private string search = "";
    private int selected;
    private bool focusPending = true;
    private bool reorderPending;
    private Vector2 scroll;
    private string[] allScenes;
    private string[] filtered;
    private HashSet<string> favourites = new HashSet<string>();

    private static Texture StarTexture
    {
        get
        {
            if (starTexture == null)
            {
                GUIContent content = EditorGUIUtility.IconContent("Favorite");
                starTexture = content != null ? content.image : null;
            }
            return starTexture;
        }
    }

    [MenuItem("Tools/Scenes/Open Scene... %#o", false, 0)]
    private static void Open()
    {
        SceneSwitcherWindow window = CreateInstance<SceneSwitcherWindow>();
        window.titleContent = new GUIContent("Open Scene");

        Rect main = EditorGUIUtility.GetMainWindowPosition();
        Vector2 size = new Vector2(460f, 420f);
        window.position = new Rect(main.center.x - size.x * 0.5f, main.y + main.height * 0.25f, size.x, size.y);
        window.ShowUtility();
    }

    private void OnEnable()
    {
        allScenes = AssetDatabase.FindAssets("t:Scene")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.StartsWith("Assets/"))
            .OrderBy(p => p)
            .ToArray();

        Refilter();
    }

    private void OnLostFocus()
    {
        Close();
    }

    private void OnGUI()
    {
        HandleKeys();

        EditorGUILayout.Space(4f);
        GUI.SetNextControlName(SearchControl);
        string next = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
        if (next != search)
        {
            search = next;
            selected = 0;
            Refilter();
        }

        if (focusPending)
        {
            EditorGUI.FocusTextInControl(SearchControl);
            focusPending = false;
        }

        EditorGUILayout.Space(2f);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        for (int i = 0; i < filtered.Length; i++)
        {
            DrawRow(i);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.LabelField("Click the star to pin a scene to the top", EditorStyles.miniLabel);

        if (reorderPending && Event.current.type == EventType.Repaint)
        {
            reorderPending = false;
            string keep = filtered.Length > 0 && selected >= 0 && selected < filtered.Length ? filtered[selected] : null;
            Refilter();
            if (keep != null)
            {
                selected = Mathf.Max(0, System.Array.IndexOf(filtered, keep));
            }
            Repaint();
        }
    }

    private void DrawRow(int index)
    {
        string path = filtered[index];
        Rect rect = EditorGUILayout.GetControlRect(false, 20f);

        if (index == selected)
        {
            EditorGUI.DrawRect(rect, new Color(0.24f, 0.48f, 0.90f, 0.35f));
        }

        Rect starRect = new Rect(rect.x + 2f, rect.y + 2f, 16f, 16f);
        bool favourite = favourites.Contains(path);

        if (Event.current.type == EventType.MouseDown && starRect.Contains(Event.current.mousePosition))
        {
            ToggleFavourite(path);
            favourites = new HashSet<string>(GetFavourites());
            selected = index;
            reorderPending = true;
            Event.current.Use();
            Repaint();
            return;
        }

        if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
        {
            selected = index;
            if (Event.current.clickCount >= 2)
            {
                OpenSelected();
            }
            Event.current.Use();
            Repaint();
        }

        DrawStar(starRect, favourite);

        Rect nameRect = new Rect(starRect.xMax + 4f, rect.y, rect.width * 0.45f, rect.height);
        Rect dirRect = new Rect(nameRect.xMax + 4f, rect.y, rect.width - nameRect.width - starRect.width - 12f, rect.height);

        GUI.Label(nameRect, Path.GetFileNameWithoutExtension(path), favourite ? EditorStyles.boldLabel : EditorStyles.label);
        GUI.Label(dirRect, Path.GetDirectoryName(path).Replace(Path.DirectorySeparatorChar, '/'), EditorStyles.miniLabel);
    }

    private static void DrawStar(Rect rect, bool favourite)
    {
        Color previous = GUI.color;
        GUI.color = favourite ? Color.white : new Color(1f, 1f, 1f, 0.22f);

        if (StarTexture != null)
        {
            GUI.DrawTexture(rect, StarTexture, ScaleMode.ScaleToFit);
        }
        else
        {
            GUI.Label(rect, favourite ? "*" : "-", EditorStyles.miniLabel);
        }

        GUI.color = previous;
    }

    private void HandleKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown)
            return;

        switch (e.keyCode)
        {
            case KeyCode.DownArrow:
                selected = Mathf.Min(selected + 1, filtered.Length - 1);
                e.Use();
                Repaint();
                break;
            case KeyCode.UpArrow:
                selected = Mathf.Max(selected - 1, 0);
                e.Use();
                Repaint();
                break;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                OpenSelected();
                e.Use();
                break;
            case KeyCode.Escape:
                Close();
                e.Use();
                break;
        }
    }

    private void OpenSelected()
    {
        if (filtered.Length == 0 || selected < 0 || selected >= filtered.Length)
            return;

        string path = filtered[selected];
        Close();

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        PushRecent(path);
    }

    private void Refilter()
    {
        favourites = new HashSet<string>(GetFavourites());

        List<string> ordered = new List<string>();
        ordered.AddRange(GetFavourites().Where(p => allScenes.Contains(p)));
        ordered.AddRange(GetRecent().Where(p => allScenes.Contains(p) && !ordered.Contains(p)));
        ordered.AddRange(allScenes.Where(p => !ordered.Contains(p)));

        if (string.IsNullOrWhiteSpace(search))
        {
            filtered = ordered.ToArray();
            return;
        }

        string[] tokens = search.Split(' ').Where(t => t.Length > 0).ToArray();
        filtered = ordered
            .Where(p => tokens.All(t => p.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0))
            .ToArray();
    }

    private static string[] GetFavourites()
    {
        if (!EditorPrefs.HasKey(FavouritesPref))
            return DefaultFavourites;

        string raw = EditorPrefs.GetString(FavouritesPref, "");
        return string.IsNullOrEmpty(raw) ? new string[0] : raw.Split('|');
    }

    private static void ToggleFavourite(string path)
    {
        List<string> favourites = GetFavourites().ToList();
        if (!favourites.Remove(path))
        {
            favourites.Add(path);
        }
        EditorPrefs.SetString(FavouritesPref, string.Join("|", favourites));
    }

    private static string[] GetRecent()
    {
        string raw = EditorPrefs.GetString(RecentPref, "");
        return string.IsNullOrEmpty(raw) ? new string[0] : raw.Split('|');
    }

    private static void PushRecent(string path)
    {
        List<string> recent = GetRecent().Where(p => p != path).ToList();
        recent.Insert(0, path);
        if (recent.Count > MaxRecent)
        {
            recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
        }
        EditorPrefs.SetString(RecentPref, string.Join("|", recent));
    }
}
