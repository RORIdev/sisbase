using DSharpPlus.CommandsNext;
using DSharpPlus.CommandsNext.Attributes;
using System.Threading.Tasks;

namespace sisbase.Attributes
{
	/// <summary>
	/// Attribute that checks if the command is run on the MASTER guild
	/// </summary>
	public class OniiSanAttribute : CheckBaseAttribute
	{
#pragma warning disable CS1998, CS1591

		public override async Task<bool> ExecuteCheckAsync(CommandContext ctx, bool help) => ctx.Guild.Id.Equals(SisbaseBot.Instance.SisbaseConfiguration.Data.MasterId);

#pragma warning restore CS1998
	}
}