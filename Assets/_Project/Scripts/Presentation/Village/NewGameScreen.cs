using System;
using System.Collections.Generic;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using NinjaRealm.Presentation.Battle;
using UnityEngine;
using UnityEngine.UIElements;

namespace NinjaRealm.Presentation.Village
{
    /// <summary>
    /// The new game screen: the player names their clan and picks its home village before the
    /// village opens. It shows while the save <see cref="ProfileService.NeedsFounding"/>, so a
    /// save from before this screen is asked once too, and keeps its progress. Layout:
    /// UI/Resources/UI/NewGame.uxml.
    /// </summary>
    [RequireComponent(typeof(VillageController))]
    public sealed class NewGameScreen : MonoBehaviour
    {
        static readonly string[] NameStarts =
        {
            "Silent", "Crimson", "Hidden", "Iron", "Ashen", "Golden", "Shadow", "Jade", "Storm", "Pale",
            "Night", "Swift", "Frost", "Burning", "Quiet", "Black", "Silver", "Wild",
        };

        static readonly string[] NameEnds =
        {
            "Moon", "Fang", "Lotus", "Tide", "Wind", "Crane", "Blade", "Mist", "Lantern", "Peak",
            "Fox", "Ember", "River", "Oak", "Hawk", "Stone", "Petal", "Viper",
        };

        VillageController controller;
        UIDocument document;
        VisualElement overlay;
        TextField nameField;
        Label nameNote;
        VisualElement villageCards;
        Label summary;
        Button found;
        UiMessage message;
        readonly List<(HomeVillage village, VisualElement card)> cards = new List<(HomeVillage, VisualElement)>();
        HomeVillage selected;
        bool shown;
        bool forced;
        bool typed;
        readonly System.Random names = new System.Random();

        /// <summary>True while the screen is up.</summary>
        public bool IsOpen => shown;

        void Awake()
        {
            controller = GetComponent<VillageController>();
        }

        void OnEnable()
        {
            if (document == null) document = GameUi.Create(transform, "NewGame", 30f);
            if (document == null) return;

            var root = document.rootVisualElement;
            overlay = root.Q("overlay");
            nameField = root.Q<TextField>("clanName");
            nameNote = root.Q<Label>("nameNote");
            villageCards = root.Q("villages");
            summary = root.Q<Label>("summary");
            found = root.Q<Button>("found");
            message = new UiMessage(root.Q<Label>("message"));
            if (overlay == null || nameField == null || villageCards == null || found == null)
            {
                Debug.LogError("[Village] NewGame.uxml is missing an element the new game screen needs.", this);
                document = null;
                return;
            }

            nameField.maxLength = ClanName.MaxLength;
            nameField.RegisterValueChangedCallback(_ =>
            {
                typed = true;
                Refresh();
            });
            nameField.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            root.Q<Button>("randomName").clicked += RandomName;
            found.clicked += Found;
            shown = false;
            GameUi.Show(overlay, false);
        }

        void OnDisable()
        {
            if (controller != null) controller.Founding = false;
            shown = false;
        }

        /// <summary>Shows the screen even for a clan that has a name, to test it or to rename the clan.</summary>
        public void Open()
        {
            forced = true;
        }

        void Update()
        {
            if (document == null) return;
            var profile = controller.Profile;
            var open = profile != null && (forced || profile.NeedsFounding);
            controller.Founding = open;
            if (open == shown) return;

            shown = open;
            GameUi.Show(overlay, open);
            if (!open) return;

            controller.Close();
            Build(profile);
        }

        void Build(ProfileService profile)
        {
            message.Clear();
            typed = false;
            nameField.SetValueWithoutNotify(profile.ClanName);
            selected = profile.HomeVillage;

            cards.Clear();
            villageCards.Clear();
            var villages = profile.Progression.Villages;
            GameUi.Show(villageCards, villages.Count > 0);
            for (var i = 0; i < villages.Count; i++)
            {
                var village = villages[i];
                if (village == null) continue;
                var card = Card(village);
                card.EnableInClassList("village-card--last", i == villages.Count - 1);
                cards.Add((village, card));
                villageCards.Add(card);
            }

            Refresh();
            nameField.schedule.Execute(() => nameField.Focus()).StartingIn(50);
        }

        VisualElement Card(HomeVillage village)
        {
            var colour = village.element != null ? village.element.Color : Color.grey;
            var hex = PrototypeGui.Hex(colour);
            var elementName = village.element != null ? village.element.DisplayName : "No element";

            var card = GameUi.Element("village-card");
            card.AddManipulator(new Clickable(() =>
            {
                selected = village;
                message.Clear();
                Refresh();
            }));

            var stripe = GameUi.Element("village-card__stripe");
            stripe.style.backgroundColor = colour;
            card.Add(stripe);

            var head = GameUi.Element("village-card__head");
            var crest = GameUi.Label(village.displayName.Substring(0, 1), "village-card__crest");
            crest.style.backgroundColor = colour * 0.8f;
            head.Add(crest);
            var titles = GameUi.Element();
            titles.Add(GameUi.Label(village.displayName, "village-card__name"));
            var element = GameUi.Label($"{elementName} village", "village-card__element");
            element.style.color = Color.Lerp(colour, Color.white, 0.25f);
            titles.Add(element);
            head.Add(titles);
            card.Add(head);

            card.Add(GameUi.Label(village.description, "village-card__text", "wrap"));
            card.Add(GameUi.Label($"<b>Dojo:</b> {elementName} ninjas show up {Affinity(village.dojoAffinity)}.", "village-card__line", "wrap"));

            var tiers = village.element != null ? village.element.Tiers : null;
            if (tiers != null && tiers.Count > 0 && tiers[0] != null)
                card.Add(GameUi.Label($"<b><color=#{hex}>{elementName} ({tiers[0].requiredCount}):</color></b> {tiers[0].description}", "village-card__line", "wrap"));

            var ninjas = NinjasOf(village);
            if (ninjas.Count > 0) card.Add(GameUi.Label($"<b>Its ninjas:</b> {string.Join(", ", ninjas)}", "village-card__line", "wrap"));

            card.Add(GameUi.Label("Choose", "village-card__pick"));
            return card;
        }

        /// <summary>How much more often the village's ninjas show up in the Dojo, in words.</summary>
        static string Affinity(int basisPoints)
        {
            if (basisPoints == 2 * ProgressionDefinition.BasisPoints) return "twice as often";
            if (basisPoints == 3 * ProgressionDefinition.BasisPoints) return "three times as often";
            var times = basisPoints / (float)ProgressionDefinition.BasisPoints;
            return times > 1f ? $"{times:0.#} times as often" : "as often as any other";
        }

        /// <summary>The names of the ninjas of a village's element, by rarity then name.</summary>
        List<string> NinjasOf(HomeVillage village)
        {
            var ninjas = new List<NinjaDefinition>();
            var content = controller.Content;
            if (content != null)
                foreach (var ninja in content.GetAll<NinjaDefinition>())
                    if (village.IsHome(ninja)) ninjas.Add(ninja);
            ninjas.Sort((a, b) => a.Rarity != b.Rarity ? a.Rarity.CompareTo(b.Rarity) : string.CompareOrdinal(a.DisplayName, b.DisplayName));
            return ninjas.ConvertAll(n => n.DisplayName);
        }

        void Refresh()
        {
            var profile = controller.Profile;
            if (profile == null) return;

            foreach (var (village, card) in cards)
            {
                var on = village == selected;
                card.EnableInClassList("village-card--selected", on);
                card.Q<Label>(className: "village-card__pick").text = on ? "Your home" : "Choose";
            }

            var name = ClanName.Clean(nameField.value);
            var problem = ClanName.Problem(name);
            nameNote.text = typed && problem != null ? $"<color=#FF9980>{problem}</color>" : $"{ClanName.MinLength} to {ClanName.MaxLength} letters, digits, spaces, hyphens or apostrophes.";

            var needsVillage = profile.Progression.Villages.Count > 0;
            if (problem != null) summary.text = "<color=#9ca3b0>Name your clan.</color>";
            else if (needsVillage && selected == null) summary.text = $"The <b>{name}</b> clan needs a home. <color=#9ca3b0>Choose a village.</color>";
            else summary.text = selected != null ? $"The <b>{name}</b> clan of <b>{selected.displayName}</b>." : $"The <b>{name}</b> clan.";

            found.SetEnabled(problem == null && (!needsVillage || selected != null));
        }

        void OnKey(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            Found();
            evt.StopPropagation();
        }

        void RandomName()
        {
            string name;
            do
            {
                name = $"{NameStarts[names.Next(NameStarts.Length)]} {NameEnds[names.Next(NameEnds.Length)]}";
            }
            while (name.Length > ClanName.MaxLength);

            typed = true;
            nameField.value = name;
        }

        void Found()
        {
            var profile = controller.Profile;
            if (profile == null) return;
            if (!profile.TryFoundClan(nameField.value, selected, out var text))
            {
                typed = true;
                Refresh();
                message.Show(false, text);
                return;
            }

            Debug.Log($"[Village] {text}");
            forced = false;
            profile.RefreshDojoIfNewDay(DateTime.Now);
            controller.ApplyHomeVillage();
        }
    }
}
