using System;
using NinjaRealm.Combat;
using NinjaRealm.Core.Content;
using NinjaRealm.Core.Definitions;
using NinjaRealm.Core.Progression;
using UnityEngine;

namespace NinjaRealm.Presentation.Battle
{
    /// <summary>
    /// The roster's three panels, drawn with IMGUI into any rect: the ninjas with their level ups
    /// (the Shrine), the Dojo's recruits, and the weapons and forge (the Blacksmith). Shared by the
    /// battle scene's roster screen and the village's buildings.
    /// </summary>
    public sealed class RosterPanels
    {
        readonly Func<ProfileService> profile;
        readonly Func<ContentDatabase> content;
        Vector2 scroll;
        string message;
        bool messageIsProblem;
        float messageUntil;

        public RosterPanels(Func<ProfileService> profile, Func<ContentDatabase> content)
        {
            this.profile = profile;
            this.content = content;
        }

        ProfileService Profile => profile();

        /// <summary>Call when switching panels, so the new one starts at the top.</summary>
        public void ResetScroll() => scroll = Vector2.zero;

        /// <summary>Shows the result of the last action for a few seconds.</summary>
        public void Show(bool succeeded, string text)
        {
            message = text;
            messageIsProblem = !succeeded;
            messageUntil = Time.unscaledTime + 4f;
        }

        /// <summary>Draws the last action's result, if it is still showing.</summary>
        public void DrawMessage(Rect rect)
        {
            if (string.IsNullOrEmpty(message) || Time.unscaledTime >= messageUntil) return;
            var color = messageIsProblem ? new Color(1f, 0.6f, 0.5f) : new Color(0.6f, 1f, 0.65f);
            GUI.Label(rect, $"<color=#{PrototypeGui.Hex(color)}>{message}</color>", PrototypeGui.Centered);
        }

        /// <summary>Your ninjas, with a level up button each. The Shrine.</summary>
        public void DrawNinjas(Rect area)
        {
            var progression = Profile.Progression;
            var ninjas = Profile.OwnedNinjas();
            GUI.Label(new Rect(area.x, area.y, area.width, 20f),
                $"<b>{ninjas.Count} / {progression.RosterLimit}</b> ninjas. Levels cost Karma; each adds 4% health and attack damage. Recruiting a ninja you own gives a shard toward its next star.",
                PrototypeGui.Small);

            const float rowHeight = 54f;
            var list = new Rect(area.x, area.y + 26f, area.width, area.height - 26f);
            scroll = GUI.BeginScrollView(list, scroll, new Rect(0f, 0f, list.width - 18f, ninjas.Count * rowHeight));
            var health = Stat(StatIds.Health);
            var attack = Stat(StatIds.AttackDamage);
            for (var i = 0; i < ninjas.Count; i++)
            {
                var ninja = ninjas[i];
                var owned = Profile.Find(ninja);
                var rank = owned.Rank;
                var row = new Rect(0f, i * rowHeight, list.width - 18f, rowHeight - 6f);
                PrototypeGui.Fill(row, PrototypeGui.CardColor, true);
                PrototypeGui.Fill(new Rect(row.x, row.y, 4f, row.height), ninja.Element != null ? ninja.Element.Color : Color.grey);

                GUI.Label(new Rect(row.x + 12f, row.y + 4f, 200f, 22f),
                    $"<b>{ninja.DisplayName}</b>  <color=#{PrototypeGui.Hex(LineupScreen.RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>", PrototypeGui.Label);
                GUI.Label(new Rect(row.x + 12f, row.y + 25f, 200f, 18f), $"{ninja.Element?.DisplayName} {ninja.Class?.DisplayName}", PrototypeGui.Small);

                GUI.Label(new Rect(row.x + 200f, row.y + 4f, 190f, 22f), LineupScreen.RankText(rank), PrototypeGui.Label);
                var next = progression.ShardsForNextStar(rank.Stars);
                var shards = next > 0 ? $"{owned.shards} / {next} shards to {rank.Stars + 1} stars" : "Most stars";
                GUI.Label(new Rect(row.x + 200f, row.y + 25f, 190f, 18f), $"{progression.Belt(rank.Level)} belt.  {shards}", PrototypeGui.Small);

                var multiplier = progression.StatMultiplier(rank.Level, rank.Stars);
                var hp = ninja.GetBaseStat(health) * multiplier / ProgressionDefinition.BasisPoints;
                var ad = ninja.GetBaseStat(attack) * multiplier / ProgressionDefinition.BasisPoints;
                GUI.Label(new Rect(row.x + 420f, row.y + 4f, 160f, 22f), $"Health <b>{hp:0}</b>", PrototypeGui.Small);
                GUI.Label(new Rect(row.x + 420f, row.y + 25f, 160f, 18f), $"Attack <b>{ad:0}</b>", PrototypeGui.Small);

                var cost = progression.LevelCost(rank.Level);
                var button = new Rect(row.xMax - 190f, row.y + 9f, 178f, 30f);
                if (cost < 0)
                {
                    GUI.Label(button, "Level cap", PrototypeGui.Centered);
                    continue;
                }

                GUI.enabled = Profile.Balance(progression.Karma) >= cost;
                if (GUI.Button(button, $"Level up  ({cost} Karma)")) Show(Profile.TryLevelUp(ninja, out var result), result);
                GUI.enabled = true;
            }

            GUI.EndScrollView();
        }

        /// <summary>The day's four recruits and the direct buy list. The Dojo.</summary>
        public void DrawDojo(Rect area)
        {
            var progression = Profile.Progression;
            var gold = Profile.Balance(progression.Gold);
            GUI.Label(new Rect(area.x, area.y, area.width - 200f, 20f),
                "Four recruits, new every day. A ninja you already own becomes a shard toward its next star.", PrototypeGui.Small);
            GUI.enabled = gold >= progression.DojoRefreshCost;
            if (GUI.Button(new Rect(area.xMax - 190f, area.y - 4f, 190f, 26f), $"New recruits  ({progression.DojoRefreshCost} Gold)"))
                Show(Profile.TryRefreshDojo(out var refreshed), refreshed);
            GUI.enabled = true;

            var offers = Profile.DojoOffers();
            var count = Mathf.Max(1, offers.Count);
            var cardWidth = (area.width - (count - 1) * 10f) / count;
            var cardY = area.y + 30f;
            for (var i = 0; i < offers.Count; i++)
            {
                var card = new Rect(area.x + i * (cardWidth + 10f), cardY, cardWidth, 150f);
                var ninja = offers[i];
                PrototypeGui.Fill(card, ninja == null ? PrototypeGui.DisabledColor : PrototypeGui.CardColor, true);
                if (ninja == null)
                {
                    GUI.Label(card, "<color=#9a9a9a>Recruited</color>", PrototypeGui.Centered);
                    continue;
                }

                PrototypeGui.Fill(new Rect(card.x, card.y, card.width, 4f), ninja.Element != null ? ninja.Element.Color : Color.grey);
                DrawNinjaSummary(ninja, card.x + 10f, card.y + 10f, card.width - 20f);

                var price = progression.RecruitPrice(ninja.Rarity);
                var problem = Profile.CannotGain(ninja);
                GUI.enabled = problem == null && gold >= price;
                if (GUI.Button(new Rect(card.x + 10f, card.yMax - 38f, card.width - 20f, 28f), $"Recruit  ({price} Gold)"))
                    Show(Profile.TryRecruitOffer(i, out var recruited), recruited);
                GUI.enabled = true;
            }

            var y = cardY + 166f;
            GUI.Label(new Rect(area.x, y, area.width, 22f), "<b>Buy directly</b>  Common and Rare ninjas, at a higher price", PrototypeGui.Label);
            y += 26f;

            var direct = Profile.DirectBuyNinjas();
            const float rowHeight = 30f;
            var list = new Rect(area.x, y, area.width, area.yMax - y);
            scroll = GUI.BeginScrollView(list, scroll, new Rect(0f, 0f, list.width - 18f, direct.Count * rowHeight));
            for (var i = 0; i < direct.Count; i++)
            {
                var ninja = direct[i];
                var row = new Rect(0f, i * rowHeight, list.width - 18f, rowHeight - 4f);
                PrototypeGui.Fill(row, PrototypeGui.CardColor);
                GUI.Label(new Rect(row.x + 10f, row.y + 3f, 260f, 22f),
                    $"<b>{ninja.DisplayName}</b>  <color=#{PrototypeGui.Hex(LineupScreen.RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>  {ninja.Element?.DisplayName} {ninja.Class?.DisplayName}",
                    PrototypeGui.Small);
                GUI.Label(new Rect(row.x + 290f, row.y + 3f, 300f, 22f), OwnedText(ninja), PrototypeGui.Small);

                var price = progression.DirectBuyPrice(ninja.Rarity);
                GUI.enabled = Profile.CannotGain(ninja) == null && gold >= price;
                if (GUI.Button(new Rect(row.xMax - 170f, row.y + 2f, 160f, 22f), $"Buy  ({price} Gold)"))
                    Show(Profile.TryBuyDirect(ninja, out var bought), bought);
                GUI.enabled = true;
            }

            GUI.EndScrollView();
        }

        void DrawNinjaSummary(NinjaDefinition ninja, float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, width, 22f), $"<b>{ninja.DisplayName}</b>", PrototypeGui.Title);
            GUI.Label(new Rect(x, y + 22f, width, 18f),
                $"<color=#{PrototypeGui.Hex(LineupScreen.RarityColor(ninja.Rarity))}>{ninja.Rarity}</color>  {ninja.Element?.DisplayName} {ninja.Class?.DisplayName}",
                PrototypeGui.Small);
            GUI.Label(new Rect(x, y + 40f, width, 36f), OwnedText(ninja), PrototypeGui.Wrap);
            if (ninja.Skill != null)
                GUI.Label(new Rect(x, y + 58f, width, 18f), $"Skill: {ninja.Skill.DisplayName}", PrototypeGui.Small);
        }

        /// <summary>What getting this ninja would do: a new ninja, a shard, or nothing.</summary>
        string OwnedText(NinjaDefinition ninja)
        {
            var owned = Profile.Find(ninja);
            if (owned == null)
                return Profile.RosterFull ? "<color=#ff9980>Roster full</color>" : "<color=#99ff99>New</color>";
            var next = Profile.Progression.ShardsForNextStar(owned.stars);
            return next > 0 ? $"Owned: +1 shard ({owned.shards} / {next})" : "Owned, most stars";
        }

        /// <summary>Your weapons and the forge. The Blacksmith.</summary>
        public void DrawArmory(Rect area)
        {
            var progression = Profile.Progression;
            var owned = Profile.OwnedWeapons();
            var recipes = Profile.Recipes();
            var half = (area.width - 20f) / 2f;

            GUI.Label(new Rect(area.x, area.y, half, 22f), "<b>Your weapons</b>  wins can drop components", PrototypeGui.Label);
            var y = area.y + 28f;
            if (owned.Count == 0) GUI.Label(new Rect(area.x, y, half, 20f), "None yet. Win fights to find components.", PrototypeGui.Small);
            foreach (var weapon in owned)
            {
                var row = new Rect(area.x, y, half, 44f);
                var combined = weapon.Tier == WeaponTier.Combined;
                PrototypeGui.Fill(row, combined ? new Color(0.26f, 0.22f, 0.14f, 0.95f) : PrototypeGui.CardColor);
                GUI.Label(new Rect(row.x + 10f, row.y + 3f, half - 70f, 20f), $"<b>{weapon.DisplayName}</b>", PrototypeGui.Label);
                GUI.Label(new Rect(row.xMax - 60f, row.y + 3f, 50f, 20f), $"x {Profile.WeaponCount(weapon)}", PrototypeGui.Label);
                GUI.Label(new Rect(row.x + 10f, row.y + 22f, half - 20f, 18f), weapon.Description, PrototypeGui.Small);
                y += 48f;
                if (y > area.yMax - 44f) break;
            }

            var x = area.x + half + 20f;
            GUI.Label(new Rect(x, area.y, half, 22f), $"<b>Forge</b>  two components and {progression.ForgeCost} Steel", PrototypeGui.Label);
            y = area.y + 28f;
            foreach (var recipe in recipes)
            {
                var row = new Rect(x, y, half, 64f);
                PrototypeGui.Fill(row, PrototypeGui.CardColor);
                GUI.Label(new Rect(row.x + 10f, row.y + 3f, half - 150f, 20f), $"<b>{recipe.DisplayName}</b>  x {Profile.WeaponCount(recipe)}", PrototypeGui.Label);
                GUI.Label(new Rect(row.x + 10f, row.y + 22f, half - 150f, 18f),
                    $"{Have(recipe.ComponentA, recipe.ComponentA == recipe.ComponentB ? 2 : 1)} + {Have(recipe.ComponentB, recipe.ComponentA == recipe.ComponentB ? 2 : 1)}",
                    PrototypeGui.Small);
                GUI.Label(new Rect(row.x + 10f, row.y + 40f, half - 150f, 18f), recipe.Description, PrototypeGui.Small);

                var problem = Profile.CannotForge(recipe);
                GUI.enabled = problem == null;
                if (GUI.Button(new Rect(row.xMax - 130f, row.y + 17f, 120f, 30f), "Forge")) Show(Profile.TryForge(recipe, out var forged), forged);
                GUI.enabled = true;
                y += 70f;
                if (y > area.yMax - 64f) break;
            }
        }

        /// <summary>A component's name, green when the player has enough of it.</summary>
        string Have(WeaponDefinition component, int needed)
        {
            var enough = Profile.WeaponCount(component) >= needed;
            var color = enough ? new Color(0.6f, 1f, 0.65f) : new Color(0.75f, 0.75f, 0.78f);
            return $"<color=#{PrototypeGui.Hex(color)}>{component.DisplayName} ({Profile.WeaponCount(component)})</color>";
        }

        StatDefinition Stat(string id)
        {
            var database = content();
            return database != null && database.TryGet<StatDefinition>(id, out var stat) ? stat : null;
        }
    }
}
