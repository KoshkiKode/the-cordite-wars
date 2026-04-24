using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CorditeWars.Core.Licensing;

/// <summary>
/// Detects whether the game was installed via a third-party storefront that
/// has its own ownership-verification mechanism. When this returns true the
/// in-game licensing gate is skipped entirely.
///
/// Detection rules — based on files the storefront drops next to the game
/// executable at install time:
///
///   * <b>Steam</b>: <c>steam_appid.txt</c> sits next to the binary in the
///     installed depot.
///   * <b>GOG</b>: <c>goggame-*.info</c> file is created by the Galaxy
///     installer (and the standalone offline installer) in the game folder.
///     GOG is DRM-free by company policy, so we honour that and don't
///     enforce activation.
///
/// We deliberately do NOT call any Steamworks/Galaxy SDK from this code —
/// that would require shipping the SDKs in non-storefront builds. The file
/// markers are created by the storefront installer and are the canonical
/// signal recommended by both platforms' partner docs.
/// </summary>
public static class StorefrontDetector
{
    public enum Storefront
    {
        Standalone,
        Steam,
        Gog,
    }

    public static Storefront Detect(string installDir)
    {
        try
        {
            if (File.Exists(Path.Combine(installDir, "steam_appid.txt")))
            {
                return Storefront.Steam;
            }
            if (Directory.Exists(installDir)
                && Directory.EnumerateFiles(installDir, "goggame-*.info").Any())
            {
                return Storefront.Gog;
            }
        }
        catch
        {
            // I/O errors → fall through to standalone (we'd rather show a
            // license prompt than silently let a possibly-pirated copy run).
        }
        return Storefront.Standalone;
    }
}

/// <summary>Outcome of <see cref="LicenseGate.RunAsync"/>.</summary>
public enum LicenseGateOutcome
{
    /// <summary>Storefront marker found — gate skipped, let the game start.</summary>
    SkippedForStorefront,
    /// <summary>Stored entitlement is valid — let the game start.</summary>
    AlreadyActivated,
    /// <summary>Activation needed — UI should prompt for a key.</summary>
    NeedsActivation,
    /// <summary>Server confirmed the cap is reached — UI should show slot list.</summary>
    MachineCapReached,
}

public sealed class LicenseGateResult
{
    public LicenseGateOutcome Outcome { get; init; }
    public Entitlement? Entitlement { get; init; }
    public StorefrontDetector.Storefront Storefront { get; init; }
    public string? Message { get; init; }
    public LicenseClient.ActiveSlot[]? ActiveSlots { get; init; }
}

/// <summary>
/// Boot-time license enforcement. The game's bootloader calls
/// <see cref="RunAsync"/> exactly once before the main menu is shown.
///
/// Behaviour:
///   1. If running under Steam or GOG → skip entirely.
///   2. If a valid entitlement is on disk and matches the current machine
///      fingerprint → start the game immediately.
///   3. If the entitlement is past its halfway point AND the network is
///      available → silently call <c>/api/activate</c> to refresh it. This
///      is the "silent background renewal" path; failures are ignored
///      (the existing entitlement is still valid).
///   4. If there's no entitlement (or it's expired/invalid) → return
///      <see cref="LicenseGateOutcome.NeedsActivation"/> so the UI can
///      prompt the user.
/// </summary>
public sealed class LicenseGate
{
    private readonly EntitlementStore _store;
    private readonly Func<LicenseClient> _clientFactory;
    private readonly string _installDir;

    public LicenseGate(EntitlementStore store, Func<LicenseClient> clientFactory, string installDir)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _installDir = installDir;
    }

    public Task<LicenseGateResult> RunAsync(CancellationToken ct = default)
    {
        var storefront = StorefrontDetector.Detect(_installDir);
        if (storefront != StorefrontDetector.Storefront.Standalone)
        {
            return Task.FromResult(new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.SkippedForStorefront,
                Storefront = storefront,
                Message = $"Storefront detected ({storefront}); license gate bypassed.",
            });
        }

        string machineId = MachineFingerprint.Compute();
        if (!_store.TryLoad(machineId, out var ent, out var reason))
        {
            return Task.FromResult(new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.NeedsActivation,
                Storefront = storefront,
                Message = reason,
            });
        }

        // Silent background renewal — fire-and-forget on a background task
        // so we don't block the boot sequence on a network call.
        if (ent!.ShouldRenew(DateTime.UtcNow))
        {
            _ = Task.Run(() => SilentRenewAsync(ct), ct);
        }

        return Task.FromResult(new LicenseGateResult
        {
            Outcome = LicenseGateOutcome.AlreadyActivated,
            Entitlement = ent,
            Storefront = storefront,
        });
    }

    /// <summary>Public so the UI can call it from the activation form.</summary>
    public async Task<LicenseGateResult> ActivateAsync(string formattedKey, CancellationToken ct = default)
    {
        if (!LicenseKey.TryParse(formattedKey, out var key, out var keyError))
        {
            return new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.NeedsActivation,
                Message = keyError,
            };
        }

        string machineId = MachineFingerprint.Compute();
        string hostname = MachineFingerprint.Hostname();
        using var client = _clientFactory();
        var resp = await client.ActivateAsync(key!.Formatted, machineId, hostname, ct).ConfigureAwait(false);
        if (!resp.Success)
        {
            return new LicenseGateResult
            {
                Outcome = resp.IsMachineCapReached
                    ? LicenseGateOutcome.MachineCapReached
                    : LicenseGateOutcome.NeedsActivation,
                Message = resp.ErrorMessage ?? resp.ErrorCode,
                ActiveSlots = resp.ActiveSlots,
            };
        }

        byte[] blob = resp.Value!.DecodeBlob();
        _store.Save(blob);
        if (!_store.TryLoad(machineId, out var ent, out var reason))
        {
            // Should not happen — Save validated, then TryLoad rejected.
            return new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.NeedsActivation,
                Message = $"Activation succeeded but stored entitlement failed verification: {reason}",
            };
        }

        return new LicenseGateResult
        {
            Outcome = LicenseGateOutcome.AlreadyActivated,
            Entitlement = ent,
        };
    }

    /// <summary>
    /// Apply an out-of-band entitlement blob (the offline-activation path).
    /// The user pastes the base64 blob from the website into the game.
    /// </summary>
    public LicenseGateResult ApplyOfflineEntitlement(string base64Blob)
    {
        try
        {
            string s = base64Blob.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            byte[] blob = Convert.FromBase64String(s);
            _store.Save(blob);
            string machineId = MachineFingerprint.Compute();
            if (!_store.TryLoad(machineId, out var ent, out var reason))
            {
                return new LicenseGateResult
                {
                    Outcome = LicenseGateOutcome.NeedsActivation,
                    Message = $"Offline entitlement rejected: {reason}",
                };
            }
            return new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.AlreadyActivated,
                Entitlement = ent,
            };
        }
        catch (Exception ex)
        {
            return new LicenseGateResult
            {
                Outcome = LicenseGateOutcome.NeedsActivation,
                Message = $"Could not parse offline entitlement: {ex.Message}",
            };
        }
    }

    private async Task SilentRenewAsync(CancellationToken ct)
    {
        try
        {
            using var client = _clientFactory();
            // The /api/renew endpoint takes the existing signed entitlement
            // blob and returns a fresh one — no key text required, no slot
            // churn, idempotent. We re-read the on-disk blob (rather than
            // re-encoding `existing`) so we don't need to round-trip back
            // through the binary format.
            byte[] currentBlob;
            try
            {
                currentBlob = System.IO.File.ReadAllBytes(_store.FilePath);
            }
            catch
            {
                return;
            }
            string b64 = Convert.ToBase64String(currentBlob)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var resp = await client.RenewAsync(b64, ct).ConfigureAwait(false);
            if (resp.Success)
            {
                _store.Save(resp.Value!.DecodeBlob());
            }
            // On any failure: keep the existing entitlement and let the
            // user discover the problem next time the game launches with
            // an expired entitlement. Silent renewal must never surface
            // errors to the player.
        }
        catch
        {
            // Swallow — silent renewal must not crash the game.
        }
    }
}
