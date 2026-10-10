using System.Collections.Generic;
using System.Threading.Tasks;

namespace AgyCliDashboard.Services;

public interface IDatabaseService
{
    Task<List<AgyChatSession>> LoadChatsAsync();
    Task<List<string>> DeleteChatSessionAsync(string conversationId);
}
