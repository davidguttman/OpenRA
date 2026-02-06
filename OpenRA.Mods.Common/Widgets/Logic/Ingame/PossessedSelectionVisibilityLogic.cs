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

using System.Linq;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	public class PossessedSelectionVisibilityLogic : ChromeLogic
	{
		[ObjectCreator.UseCtor]
		public PossessedSelectionVisibilityLogic(Widget widget, World world)
		{
			widget.IsVisible = () =>
			{
				var localClient = world.LobbyInfo.ClientWithIndex(Game.LocalClientId);
				if (localClient == null || !localClient.IsObserver)
					return true;

				return world.Selection.Actors.Any(a =>
					a.IsInWorld && !a.IsDead &&
					a.TraitOrDefault<Possessable>()?.IsPossessedBy(Game.LocalClientId) == true);
			};
		}
	}
}
