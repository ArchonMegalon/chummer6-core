using System.Text.Json.Nodes;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Tools;

public interface ISettingsStore
{
    JsonObject Load(string scope);

    void Save(string scope, JsonObject settings);

    JsonObject Load(OwnerScope owner, string scope);

    void Save(OwnerScope owner, string scope, JsonObject settings);
}
