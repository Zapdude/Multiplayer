using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Verse;

namespace Multiplayer.Client.Compatibility;

// Room settings for compat mods whose ModSettings are personal but have to agree inside a session.
// The host's copy is captured into the save; joining players get it applied for the session and their
// own settings are restored when the session stops.
public static class RoomSettings
{
    private abstract class Registration
    {
        internal abstract IExposable ForGame(Game game);
        internal abstract bool Owns(ModSettings settings);
        internal abstract void Stop();
    }

    private abstract class SettingsState : IExposable
    {
        public abstract void ExposeData();
        internal abstract void Ensure();
        internal abstract bool Apply(IExposable selected);
    }

    private sealed class Registration<T> : Registration where T : class, IExposable
    {
        internal readonly Func<IExposable> Capture;
        internal readonly Func<IExposable> Defaults;
        internal readonly Action<IExposable> ApplyValue;
        internal readonly Func<IExposable, bool> Validate;
        internal readonly Func<ModSettings> Owner;
        internal readonly string Label;

        private readonly ConditionalWeakTable<Game, State<T>> states = new();

        internal T Personal;
        internal State<T> Current;

        public Registration(
            Func<IExposable> capture,
            Func<IExposable> defaults,
            Action<IExposable> apply,
            Func<IExposable, bool> validate,
            Func<ModSettings> owner,
            string label)
        {
            Capture = capture;
            Defaults = defaults;
            ApplyValue = apply;
            Validate = validate;
            Owner = owner;
            Label = label;

            if (defaults() is not T value || !validate(value))
                throw new ArgumentException("Invalid default room-settings schema");
        }

        internal override IExposable ForGame(Game game)
        {
            return states.GetValue(game, _ => new State<T>(this));
        }

        internal override bool Owns(ModSettings settings) => Owner?.Invoke() == settings;

        internal void RememberPersonal()
        {
            Personal ??= (T)Capture();
        }

        internal override void Stop()
        {
            if (Personal == null) return;

            try
            {
                if (Current != null)
                {
                    Current.Snapshot = (T)Capture();
                    Current.Initialized = false;
                }
            }
            finally
            {
                try
                {
                    ApplyValue(Personal);
                }
                finally
                {
                    Personal = null;
                    Current = null;
                }
            }
        }
    }

    private sealed class State<T>(Registration<T> registration) : SettingsState where T : class, IExposable
    {
        internal T Snapshot;
        internal bool Initialized;

        internal override void Ensure()
        {
            if (Multiplayer.session == null || Initialized) return;

            registration.RememberPersonal();

            if (Snapshot != null)
            {
                var toApply = registration.Validate(Snapshot) ? Snapshot : (T)registration.Defaults();
                registration.ApplyValue(toApply);
            }

            Initialized = true;
            registration.Current = this;
        }

        internal override bool Apply(IExposable selected)
        {
            if (Multiplayer.session == null || selected is not T value || !registration.Validate(value))
                return false;

            Ensure();
            registration.ApplyValue(value);
            Snapshot = value;
            return true;
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving && Multiplayer.session != null)
            {
                Ensure();
                Snapshot = (T)registration.Capture();
            }

            Scribe_Deep.Look(ref Snapshot, registration.Label);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Initialized = false;
                Ensure();
            }
        }
    }

    public const int Version = 1;

    private static readonly SortedDictionary<string, Registration> registrations = new(StringComparer.Ordinal);

    public static void Register(
        string key,
        Type snapshotType,
        Func<IExposable> capture,
        Func<IExposable> defaults,
        Action<IExposable> apply,
        Func<IExposable, bool> validate,
        Func<ModSettings> personalSettings,
        string saveLabel)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(saveLabel))
            throw new ArgumentException("Stable settings key and save label are required");
        if (snapshotType == null || !snapshotType.IsClass || !typeof(IExposable).IsAssignableFrom(snapshotType))
            throw new ArgumentException("Snapshot must be an exposable reference type");

        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(defaults);
        ArgumentNullException.ThrowIfNull(apply);
        ArgumentNullException.ThrowIfNull(validate);

        var registration = (Registration)Activator.CreateInstance(
            typeof(Registration<>).MakeGenericType(snapshotType),
            capture, defaults, apply, validate, personalSettings, saveLabel);

        registrations.Add(key, registration);
    }

    public static IExposable ForGame(string key, Game game)
    {
        return registrations[key].ForGame(game ?? throw new ArgumentNullException(nameof(game)));
    }

    public static void Ensure(IExposable state) => ((SettingsState)state).Ensure();

    public static bool Apply(IExposable state, IExposable selected) => ((SettingsState)state).Apply(selected);

    public static bool AllowPersonalWrite(ModSettings settings)
    {
        if (Multiplayer.session == null) return true;

        foreach (var registration in registrations.Values)
            if (registration.Owns(settings))
                return false;

        return true;
    }

    internal static void Stopping()
    {
        foreach (var registration in registrations)
        {
            try
            {
                registration.Value.Stop();
            }
            catch (Exception e)
            {
                Log.Error($"Multiplayer room settings cleanup failed for {registration.Key}: {e}");
            }
        }
    }
}
