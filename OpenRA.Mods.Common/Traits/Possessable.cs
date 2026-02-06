#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class PossessableInfo : TraitInfo
	{
		[Desc("Selection bar color when the unit is possessed.")]
		public readonly Color IndicatorColor = Color.Orange;

		[Desc("Percentage of max HP to regenerate per step while possessed.")]
		public readonly int RegenPercentageStep = 0;

		[Desc("Time in ticks to wait between each regeneration step.")]
		public readonly int RegenDelay = 0;

		[Desc("Regenerate if current health is below this percentage of full health.")]
		public readonly int RegenStartIfBelow = 100;

		[Desc("Time in ticks to wait after taking damage before regenerating.")]
		public readonly int RegenDamageCooldown = 0;

		[Desc("Optional lobby option id that overrides RegenDelay.")]
		public readonly string RegenOptionId = null;

		[Desc("Experience multiplier to apply while possessed.")]
		public readonly int PossessedXpMultiplier = 100;

		[Desc("Optional lobby option id that overrides PossessedXpMultiplier.")]
		public readonly string XpMultiplierOptionId = null;

		[Desc("Number of rank bonus condition stacks to grant while possessed.")]
		public readonly int PossessedRankBonus = 0;

		[Desc("Condition to grant for each possession rank bonus stack.")]
		public readonly string RankBonusCondition = null;

		[Desc("Optional lobby option id that overrides PossessedRankBonus.")]
		public readonly string RankBonusOptionId = null;

		public override object Create(ActorInitializer init) { return new Possessable(init.Self, this); }
	}

	public class Possessable : IResolveOrder, ISync, IProvideTooltipInfo, ISelectionBar, IRenderAnnotationsWhenSelected,
		INotifyCreated, INotifyKilled, INotifyActorDisposing, ITick, INotifyDamage, IGainsExperienceModifier
	{
		public const string PossessOrder = "Possess";
		public const string ReleaseOrder = "Release";
		public const string PossessedCondition = "possessed";

		const int NoController = -1;

		readonly Actor self;
		readonly PossessableInfo info;
		readonly IHealth health;
		readonly List<int> rankBonusTokens = new();
		ExternalCondition externalCondition;
		int conditionToken = Actor.InvalidConditionToken;

		[VerifySync]
		int controllerClientId = NoController;

		[VerifySync]
		int regenTicks;

		[VerifySync]
		int regenDamageTicks;

		int regenDelayTicks;
		int regenPercentStep;
		int regenStartIfBelow;
		int regenDamageCooldown;
		int possessedXpMultiplier;
		int possessedRankBonus;

		public Possessable(Actor self, PossessableInfo info)
		{
			this.self = self;
			this.info = info;
			health = self.TraitOrDefault<IHealth>();
		}

		public int ControllerClientId => controllerClientId;
		public bool IsPossessed => controllerClientId != NoController;

		public bool IsPossessedBy(int clientId) => controllerClientId == clientId;

		void INotifyCreated.Created(Actor actor)
		{
			externalCondition = actor.TraitsImplementing<ExternalCondition>()
				.FirstOrDefault(t => t.Info.Condition == PossessedCondition);

			LoadLobbyOptions(actor.World);
		}

		void IResolveOrder.ResolveOrder(Actor actor, Order order)
		{
			switch (order.OrderString)
			{
				case PossessOrder:
				{
					var clientId = (int)order.ExtraData;
					if (IsPossessed)
						return;

					if (self.Owner == null || !self.Owner.IsBot)
						return;

					ReleaseOtherPossessions(clientId);
					ApplyPossession(clientId);
					break;
				}

				case ReleaseOrder:
				{
					var clientId = (int)order.ExtraData;
					if (IsPossessedBy(clientId))
						ClearPossession();
					break;
				}
			}
		}

		void ReleaseOtherPossessions(int clientId)
		{
			foreach (var actor in self.World.ActorsHavingTrait<Possessable>(p => p.IsPossessedBy(clientId)))
			{
				if (actor == self)
					continue;

				actor.TraitOrDefault<Possessable>()?.Release();
			}
		}

		public void Release()
		{
			ClearPossession();
		}

		void ApplyPossession(int clientId)
		{
			controllerClientId = clientId;
			self.CancelActivity();
			EnsurePossessedCondition();
			ApplyRankBonus();
			regenTicks = regenDelayTicks;
			regenDamageTicks = 0;
		}

		void ClearPossession()
		{
			controllerClientId = NoController;
			regenTicks = 0;
			regenDamageTicks = 0;
			ClearRankBonus();
			if (conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);
		}

		void ApplyRankBonus()
		{
			if (possessedRankBonus <= 0 || string.IsNullOrWhiteSpace(info.RankBonusCondition))
				return;

			for (var i = 0; i < possessedRankBonus; i++)
				rankBonusTokens.Add(self.GrantCondition(info.RankBonusCondition));
		}

		void ClearRankBonus()
		{
			if (rankBonusTokens.Count == 0)
				return;

			foreach (var token in rankBonusTokens)
				self.RevokeCondition(token);

			rankBonusTokens.Clear();
		}

		void LoadLobbyOptions(World world)
		{
			regenDelayTicks = info.RegenDelay;
			regenPercentStep = info.RegenPercentageStep;
			regenStartIfBelow = info.RegenStartIfBelow;
			regenDamageCooldown = info.RegenDamageCooldown;
			possessedXpMultiplier = info.PossessedXpMultiplier;
			possessedRankBonus = info.PossessedRankBonus;

			if (!string.IsNullOrWhiteSpace(info.RegenOptionId))
				regenDelayTicks = ReadIntOption(world, info.RegenOptionId, regenDelayTicks);

			if (!string.IsNullOrWhiteSpace(info.XpMultiplierOptionId))
				possessedXpMultiplier = ReadIntOption(world, info.XpMultiplierOptionId, possessedXpMultiplier);

			if (!string.IsNullOrWhiteSpace(info.RankBonusOptionId))
				possessedRankBonus = ReadIntOption(world, info.RankBonusOptionId, possessedRankBonus);

			if (regenDelayTicks < 0)
				regenDelayTicks = 0;

			if (regenPercentStep < 0)
				regenPercentStep = 0;

			if (regenStartIfBelow < 0)
				regenStartIfBelow = 0;
			else if (regenStartIfBelow > 100)
				regenStartIfBelow = 100;

			if (regenDamageCooldown < 0)
				regenDamageCooldown = 0;

			if (possessedXpMultiplier <= 0)
				possessedXpMultiplier = 100;

			if (possessedRankBonus < 0)
				possessedRankBonus = 0;
		}

		static int ReadIntOption(World world, string optionId, int fallback)
		{
			var raw = world.LobbyInfo.GlobalSettings.OptionOrDefault(optionId, fallback.ToStringInvariant());
			return Exts.TryParseInt32Invariant(raw, out var value) ? value : fallback;
		}

		void EnsurePossessedCondition()
		{
			if (conditionToken != Actor.InvalidConditionToken)
				return;

			if (externalCondition != null)
				conditionToken = externalCondition.GrantCondition(self, this);
			else
				conditionToken = self.GrantCondition(PossessedCondition);
		}

		void INotifyKilled.Killed(Actor actor, AttackInfo e)
		{
			ClearPossession();
		}

		void INotifyActorDisposing.Disposing(Actor actor)
		{
			ClearPossession();
		}

		void ITick.Tick(Actor actor)
		{
			if (!IsPossessed || actor.IsDead || health == null)
				return;

			if (regenDelayTicks <= 0 || regenPercentStep <= 0)
				return;

			if (health.HP >= regenStartIfBelow * (long)health.MaxHP / 100)
				return;

			if (regenDamageTicks > 0)
			{
				--regenDamageTicks;
				return;
			}

			if (--regenTicks <= 0)
			{
				regenTicks = regenDelayTicks;
				var heal = regenPercentStep * (long)health.MaxHP / 100;
				if (heal <= 0)
					return;

				self.InflictDamage(self, new Damage((int)-heal));
			}
		}

		void INotifyDamage.Damaged(Actor actor, AttackInfo e)
		{
			if (!IsPossessed || regenDamageCooldown <= 0 || e.Damage.Value <= 0)
				return;

			regenDamageTicks = regenDamageCooldown;
		}

		int IGainsExperienceModifier.GetGainsExperienceModifier()
		{
			return IsPossessed ? possessedXpMultiplier : 100;
		}

		bool IProvideTooltipInfo.IsTooltipVisible(Player forPlayer) => IsPossessed;

		string IProvideTooltipInfo.TooltipText
		{
			get
			{
				var client = self.World.LobbyInfo.ClientWithIndex(controllerClientId);
				return client == null ? "Possessed" : $"Possessed by {client.Name}";
			}
		}

		float ISelectionBar.GetValue() => IsPossessed ? 1f : 0f;
		Color ISelectionBar.GetColor() => info.IndicatorColor;
		bool ISelectionBar.DisplayWhenEmpty => false;

		IEnumerable<IRenderable> IRenderAnnotationsWhenSelected.RenderAnnotations(Actor actor, WorldRenderer wr)
		{
			if (!IsPossessed)
				return [];

			var decorations = actor.TraitsImplementing<ISelectionDecorations>().FirstEnabledTraitOrDefault();
			if (decorations == null)
				return [];

			return decorations.RenderSelectionAnnotations(actor, wr, info.IndicatorColor);
		}

		bool IRenderAnnotationsWhenSelected.SpatiallyPartitionable => true;
	}
}
