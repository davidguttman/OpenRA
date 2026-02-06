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
using OpenRA.Mods.Common.Lint;
using OpenRA.Mods.Common.Traits;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets.Logic.Ingame
{
	[ChromeLogicArgsHotkeys("PossessUnitKey", "ReleasePossessionKey")]
	public class PossessionHotkeyLogic : ChromeLogic
	{
		readonly HotkeyReference possessUnitKey = new();
		readonly HotkeyReference releasePossessionKey = new();
		readonly World world;

		[ObjectCreator.UseCtor]
		public PossessionHotkeyLogic(Widget widget, ModData modData, World world, Dictionary<string, MiniYaml> logicArgs)
		{
			this.world = world;

			if (logicArgs.TryGetValue("PossessUnitKey", out var yaml))
				possessUnitKey = modData.Hotkeys[yaml.Value];

			if (logicArgs.TryGetValue("ReleasePossessionKey", out yaml))
				releasePossessionKey = modData.Hotkeys[yaml.Value];

			var keyhandler = widget.Get<LogicKeyListenerWidget>("WORLD_KEYHANDLER");
			keyhandler.AddHandler(HandleKey);
		}

		bool HandleKey(KeyInput e)
		{
			if (e.Event != KeyInputEvent.Down || e.IsRepeat)
				return false;

			if (possessUnitKey.IsActivatedBy(e))
				return TryPossessSelected();

			if (releasePossessionKey.IsActivatedBy(e))
				return TryRelease();

			return false;
		}

		bool TryPossessSelected()
		{
			if (world == null || world.IsGameOver)
				return false;

			var localClient = world.LobbyInfo.ClientWithIndex(Game.LocalClientId);
			if (localClient == null || !localClient.IsObserver)
				return false;

			var selected = world.Selection.Actors.Where(a => a.IsInWorld && !a.IsDead).ToArray();
			if (selected.Length != 1)
				return false;

			var actor = selected[0];
			if (actor.TraitOrDefault<Possessable>() == null)
				return false;

			world.IssueOrder(new Order(Possessable.PossessOrder, actor, false)
			{
				ExtraData = (uint)Game.LocalClientId
			});

			return true;
		}

		bool TryRelease()
		{
			if (world == null)
				return false;

			var localClient = world.LobbyInfo.ClientWithIndex(Game.LocalClientId);
			if (localClient == null || !localClient.IsObserver)
				return false;

			var possessed = world.ActorsHavingTrait<Possessable>(p => p.IsPossessedBy(Game.LocalClientId)).FirstOrDefault();
			if (possessed == null)
				return false;

			world.IssueOrder(new Order(Possessable.ReleaseOrder, possessed, false)
			{
				ExtraData = (uint)Game.LocalClientId
			});

			return true;
		}
	}
}
