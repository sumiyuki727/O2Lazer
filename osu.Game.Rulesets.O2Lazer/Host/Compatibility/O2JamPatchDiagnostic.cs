using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace osu.Game.Rulesets.O2Lazer;

internal enum O2JamPatchRegistrationState
{
    WaitingForProvider,
    Registered,
    RegistrationFailed,
}

/// <summary>
/// A runtime's patch metadata is evidence of registrations, not proof that its detour is executing.
/// </summary>
internal sealed record O2JamPatchDiagnostic(
    string Owner, string ProviderAssembly, string RuntimeAssembly, MethodInfo Target,
    O2JamPatchRegistrationState State, IReadOnlyList<string> ObservedOwners, string? InspectionError)
{
    internal static O2JamPatchDiagnostic Inspect(
        string owner, string provider, Type runtime, MethodInfo target, O2JamPatchRegistrationState state)
    {
        try
        {
            var getInfo = runtime.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static, [typeof(MethodBase)])
                ?? throw new MissingMethodException(runtime.FullName, "GetPatchInfo");
            var info = getInfo.Invoke(null, [target]);
            string[] owners = [];
            if (info != null)
            {
                var property = info.GetType().GetProperty("Owners")
                    ?? throw new MissingMemberException(info.GetType().FullName, "Owners");
                if (property.GetValue(info) is not IEnumerable<string> registeredOwners)
                    throw new InvalidOperationException("Harmony patch owners have an unsupported representation.");
                owners = registeredOwners.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            }

            return new(owner, provider, runtime.Assembly.FullName!, target, state, owners, null);
        }
        catch (Exception exception)
        {
            return new(owner, provider, runtime.Assembly.FullName!, target, state, [],
                (exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).Message);
        }
    }
}
