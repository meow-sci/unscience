using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using RabbitEars.Live;
using RabbitEars.Pal;
using RabbitEars.Tv;

namespace RabbitEars.Web;

/// <summary>
/// The /api/* endpoints. Every call is a GET with query parameters and answers JSON; a bad parameter throws
/// <see cref="ArgumentException"/>, which the server turns into a 400 with the message.
/// </summary>
public sealed class LiveApi
{
    private readonly LiveSession _session;

    public LiveApi(LiveSession session) => _session = session;

    /// <summary>Returns the JSON-serialisable answer, or null when the path is not an API endpoint.</summary>
    public object? Handle(string path, NameValueCollection query) => path switch
    {
        "/api/state" => StateReport.State(_session),
        "/api/knobs" => StateReport.Knobs(_session),
        "/api/spectrum" => Spectrum(),
        "/api/tune" => Tune(query),
        "/api/rx" => Receiver(query),
        "/api/channel" => Channel(query),
        "/api/set" => Set(query),
        "/api/look" => Look(query),
        "/api/power" => Power(),
        _ => null,
    };

    private object Spectrum() => _session.Spectrum.GetDb().Select(db => Math.Round(db, 1)).ToArray();

    private object Tune(NameValueCollection query)
    {
        if (query["slot"] is { } slotText)
        {
            int slot = (int)Number("slot", slotText);
            if (slot < 0 || slot >= _session.Plan.MaxChannels) throw new ArgumentException($"slot must be 0..{_session.Plan.MaxChannels - 1}");
            _session.Tuner.TuneToSlot(slot);
        }
        else if (query["freq"] is { } frequencyText)
        {
            _session.Tune(Number("freq", frequencyText) * 1e6);
        }
        else
        {
            throw new ArgumentException("tune needs freq=<MHz> or slot=<n>");
        }
        return StateReport.Tuner(_session);
    }

    private object Receiver(NameValueCollection query)
    {
        if (query["noise_db"] is { } noise)
            _session.NoiseDb = noise is "off" or "none" or "" ? null : Number("noise_db", noise);
        if (query["atten_db"] is { } attenuation)
            _session.Tuner.AttenuationDb = Math.Clamp(Number("atten_db", attenuation), 0, 120);
        return StateReport.Tuner(_session);
    }

    private object Channel(NameValueCollection query)
    {
        MuxProducer mux = _session.Mux ?? throw new ArgumentException("channels of a recorded band cannot be changed");
        int slot = (int)Number("slot", query["slot"] ?? throw new ArgumentException("channel needs slot=<n>"));
        LiveChannel channel = mux.Find(slot) ?? throw new ArgumentException($"no channel on slot {slot}");

        if (query["level_db"] is { } level) mux.SetLevel(slot, Math.Clamp(Number("level_db", level), -60, 12));
        PalSignalParams? changed = null;
        foreach (string? key in query.AllKeys)
        {
            if (key is null || !SignalKnobs.ByName.TryGetValue(key, out Knob? knob)) continue;
            if (channel.Encoder is null) throw new ArgumentException($"slot {slot} is fed by a sender: its picture is encoded elsewhere");
            changed = SignalKnobs.With(changed ?? channel.Encoder.Params, key, (float)knob.Number(knob.Coerce(query[key] ?? "")));
        }
        if (changed is not null) channel.Encoder!.Params = changed;
        return StateReport.Channel(channel);
    }

    private object Set(NameValueCollection query)
    {
        var changes = new Dictionary<string, string>();
        foreach (string? key in query.AllKeys)
            if (key is not null && SetKnobs.ByName.TryGetValue(key, out Knob? knob)) changes[key] = knob.Coerce(query[key] ?? "");
        if (changes.Count > 0) _session.Television.ChangeKnobs(changes, Seamless(query));
        return StateReport.SetValues(_session.Television);
    }

    private object Look(NameValueCollection query)
    {
        string name = query["name"] ?? "";
        if (name != "__reset__" && !SetKnobs.Looks.ContainsKey(name)) throw new ArgumentException($"unknown look '{name}'");
        _session.Television.ApplyLook(name, Seamless(query));
        return StateReport.SetValues(_session.Television);
    }

    private object Power()
    {
        _session.Television.Launch(seamless: false);
        return StateReport.SetValues(_session.Television);
    }

    private static bool Seamless(NameValueCollection query) => query["seamless"] != "0";

    private static double Number(string name, string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && !double.IsNaN(value)
            ? value : throw new ArgumentException($"{name}: '{text}' is not a number");
}
