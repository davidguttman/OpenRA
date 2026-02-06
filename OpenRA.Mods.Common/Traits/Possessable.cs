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

		public override object Create(ActorInitializer init) { return new Possessable(init.Self, this); }
	}

	public class Possessable : IResolveOrder, ISync, IProvideTooltipInfo, ISelectionBar, IRenderAnnotationsWhenSelected
	{
		public const string PossessOrder = "Possess";
		public const string ReleaseOrder = "Release";

		const int NoController = -1;

		readonly Actor self;
		readonly PossessableInfo info;

		[VerifySync]
		int controllerClientId = NoController;

		public Possessable(Actor self, PossessableInfo info)
		{
			this.self = self;
			this.info = info;
		}

		public int ControllerClientId => controllerClientId;
		public bool IsPossessed => controllerClientId != NoController;

		public bool IsPossessedBy(int clientId) => controllerClientId == clientId;

		void IResolveOrder.ResolveOrder(Actor actor, Order order)
		{
			switch (order.OrderString)
			{
				case PossessOrder:
				{
					var clientId = (int)order.ExtraData;
					if (IsPossessed && !IsPossessedBy(clientId))
						return;

					if (self.Owner == null || !self.Owner.IsBot)
						return;

					ReleaseOtherPossessions(clientId);
					controllerClientId = clientId;
					break;
				}

				case ReleaseOrder:
				{
					var clientId = (int)order.ExtraData;
					if (IsPossessedBy(clientId))
						controllerClientId = NoController;
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
			controllerClientId = NoController;
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
