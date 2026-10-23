using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LmuCareer.Core;
using LmuCareer.Core.Careers;
using LmuCareer.Core.Content;
using Microsoft.Win32;

namespace LmuCareer.App;

/// <summary>
/// What the page can ask the app to do. The page posts <c>{ id, method, args }</c>; the reply is
/// <c>{ id, ok, result }</c> or <c>{ id, ok: false, error }</c>.
/// </summary>
public sealed class ApiHost
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly MainWindow _window;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Locales _locales = Locales.Load();
    private readonly CareerStore _store = new(CareerStore.DefaultRoot);
    private readonly ContentCatalog _catalog = ContentCatalog.Default;
    private readonly ResultsWatcher _watcher;
    private LmuInstall? _install;

    // The career whose screen is open: only it listens for new results.
    private Guid? _watchedCareer;
    private string? _lastPosted;

    public ApiHost(MainWindow window)
    {
        _window = window;
        _watcher = new ResultsWatcher(window.Dispatcher);
        _watcher.Settled += OnResultsSettled;
        if (_settings.LmuRoot is { } root && LmuInstall.LooksLikeInstall(root)) UseInstall(root);
    }

    private void UseInstall(string root)
    {
        _install = LmuInstall.Scan(root);
        _watcher.Watch(_install.ResultsFolder);
    }

    /// <summary>
    /// New results files have settled: re-check the open career's armed round and tell the page
    /// when something it should show has changed (a race to count, or a qualifying session).
    /// </summary>
    private void OnResultsSettled()
    {
        if (_watchedCareer is not Guid id || _install is null) return;

        Career career;
        try
        {
            career = _store.Load(id).Career;
        }
        catch (FileNotFoundException)
        {
            return;
        }
        if (career.CurrentSeason.ArmedRound is null) return;

        var evaluation = CareerActions.CheckArmedRound(_store, career, _install.ResultsFolder, _install.RaceSavesFolder);
        // New practice: the briefing's pace advice changed, so the page redraws it.
        if (evaluation.PracticeChanged)
            _window.PostEvent(JsonSerializer.Serialize(new { @event = "practiceRead", careerId = id }, Json));
        var wrongCar = CareerActions.WrongCarRace(evaluation);
        if (evaluation.Status == RoundStatus.Waiting && evaluation.Qualifying is null && wrongCar is null) return;

        var signature = $"{id}|{evaluation.Status}|{evaluation.Race?.FileName}|{evaluation.Qualifying?.FileName}|{wrongCar?.FileName}";
        if (signature == _lastPosted) return;
        _lastPosted = signature;

        // A race saved to finish later isn't news worth flashing for.
        if (evaluation.Status is not (RoundStatus.Waiting or RoundStatus.SavedToResume) || wrongCar is not null) _window.FlashIfInBackground();
        _window.PostEvent(JsonSerializer.Serialize(new
        {
            @event = "roundUpdate",
            careerId = id,
            evaluation = EvaluationView(evaluation),
        }, Json));
    }

    public string Handle(string message)
    {
        JsonElement id = default;
        try
        {
            using var doc = JsonDocument.Parse(message);
            id = doc.RootElement.GetProperty("id").Clone();
            var method = doc.RootElement.GetProperty("method").GetString() ?? "";
            var args = doc.RootElement.TryGetProperty("args", out var a) ? a.Clone() : default;

            var result = Dispatch(method, args);
            return JsonSerializer.Serialize(new { id, ok = true, result }, Json);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException
            or KeyNotFoundException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            // A problem the player can fix comes with its message as a phrase, for the page to translate.
            return JsonSerializer.Serialize(new { id, ok = false, error = ex.Message, phrase = (ex as PlayerError)?.Phrase }, Json);
        }
        catch (Exception ex)
        {
            // A bug: logged for bug reports, and answered, so the page doesn't wait forever.
            Log($"App error: {ex}");
            return JsonSerializer.Serialize(new { id, ok = false, error = ex.Message, phrase = (Phrase?)null }, Json);
        }
    }

    private object? Dispatch(string method, JsonElement args) => method switch
    {
        "init" => Init(),
        "log" => Log(Str(args, "message")),
        "watchCareer" => WatchCareer(Str(args, "id")),
        "checkForUpdate" => CheckForUpdate(),
        "updateCardSeen" => UpdateCardSeen(Str(args, "version")),
        "browseLmu" => BrowseLmu(),
        "setLmuRoot" => SetLmuRoot(Str(args, "path")),
        "setPrefs" => SetPrefs(args),
        "openLocales" => OpenLocales(),
        "setTitleBar" => SetTitleBar(args.GetProperty("dark").GetBoolean()),
        "catalog" => CatalogView(),
        "defaultSeason" => CalendarBuilder.DefaultSeason(_catalog, Str(args, "carClass"), DriverRating.Silver,
            Strings(args, "ownedPacks"), _install, fullWec: args.TryGetProperty("fullWec", out var fullWec) && fullWec.GetBoolean()),
        "roundFor" => RoundFor(args),
        "createCareer" => CreateCareer(args),
        "listCareers" => _store.List(),
        "getCareer" => GetCareer(args),
        "renameCareer" => Change(args, c => c.Name = Str(args, "name").Trim()),
        "deleteCareer" => DeleteCareer(args),
        "exportCareer" => ExportCareer(args),
        "importCareer" => ImportCareer(),
        "armRound" => Change(args, c => RoundFlow.Arm(c.CurrentSeason, DateTimeOffset.Now)),
        "disarmRound" => Change(args, c => RoundFlow.Disarm(c.CurrentSeason.ArmedRound ?? throw new InvalidOperationException("No round is armed."))),
        "skipRound" => Change(args, c => RoundFlow.Skip(c.CurrentSeason.NextRound ?? throw new InvalidOperationException("No rounds left."))),
        "checkRound" => EvaluationView(CareerActions.CheckArmedRound(_store, Load(args), ResultsFolder, _install?.RaceSavesFolder)),
        "acceptRound" => AcceptRound(args),
        "endSeason" => EndSeason(args),
        "setOwnedContent" => Change(args, c => c.OwnedContent = Strings(args, "ownedPacks")
            .Where(p => _catalog.Packs.Any(k => k.Id == p) && p != ContentCatalog.BasePack).Distinct().ToList()),
        "acceptGuest" => Change(args, c => GuestDrives.Accept(c, Str(args, "offerId"),
            args.TryGetProperty("minutes", out var guestMinutes) && guestMinutes.ValueKind == JsonValueKind.Number ? guestMinutes.GetInt32() : null, _catalog)),
        "withdrawGuest" => Change(args, c => GuestDrives.Withdraw(c, Str(args, "offerId"))),
        "acceptTest" => Change(args, c => TestDays.Accept(c, Str(args, "offerId"), _catalog)),
        "choosePath" => Change(args, c => Progression.ChoosePath(c, Str(args, "path") == "prototype" ? CareerPath.Prototype : CareerPath.Gt, _catalog, _install)),
        "withdrawTest" => Change(args, c => TestDays.Withdraw(c, Str(args, "offerId"))),
        "readMessage" => ReadMessage(args),
        "roundAnalysis" => RoundAnalysis(args),
        "seasonStandings" => SeasonStandings(args),
        "answerInterview" => Change(args, c => Paddock.Answer(c, Str(args, "messageId"),
            Enum.Parse<InterviewTone>(Str(args, "tone")))),
        "answerApproach" => Change(args, c => Sponsorship.AnswerApproach(c, Str(args, "messageId"),
            args.TryGetProperty("sign", out var sign) && sign.GetBoolean())),
        "setNationality" => Change(args, c => c.Nationality = Nationality(Str(args, "nationality"))),
        "signSponsor" => Change(args, c => Sponsorship.Sign(c.CurrentSeason, Str(args, "sponsorId"))),
        "dropSponsor" => Change(args, c => Sponsorship.Drop(c.CurrentSeason, Str(args, "sponsorId"))),
        "sponsorLivery" => Change(args, c => Sponsorship.SetLivery(c.CurrentSeason, Str(args, "sponsorId"),
            args.TryGetProperty("running", out var running) && running.GetBoolean())),
        "startNextSeason" => StartNextSeason(args),
        "startFreeSeason" => StartFreeSeason(args),
        "changeCar" => Change(args, c => CareerActions.ChangeCar(c,
            _catalog.Cars.FirstOrDefault(car => car.Folder == Str(args, "carFolder"))
                ?? throw new KeyNotFoundException($"No car \"{Str(args, "carFolder")}\"."))),
        _ => throw new InvalidOperationException($"Unknown request \"{method}\"."),
    };

    /// <summary>
    /// Looks for a newer release in the background and tells the page if there is one. Nothing is
    /// sent but the request itself; a failure (offline, rate limited) is silently ignored.
    /// </summary>
    private object? CheckForUpdate()
    {
        if (!_settings.CheckForUpdates) return null;
        _ = Task.Run(async () =>
        {
            if (await Updates.LatestRelease() is not { } latest || !Updates.IsNewer(latest.Version)) return;
            var version = latest.Version.ToString(3);
            _window.Dispatcher.Invoke(() => _window.PostEvent(JsonSerializer.Serialize(new
            {
                @event = "updateAvailable",
                version,
                url = latest.Url,
                highlights = latest.HighlightsIn(Language),
                // The card opens once per version; after that the top bar button is the reminder.
                card = _settings.UpdateCardSeen != version,
            }, Json)));
        });
        return null;
    }

    private object? UpdateCardSeen(string version)
    {
        _settings.UpdateCardSeen = version;
        _settings.Save();
        return null;
    }

    private string ResultsFolder => _install?.ResultsFolder
        ?? throw new PlayerError("Choose your Le Mans Ultimate folder in Settings first.");

    private object? WatchCareer(string id)
    {
        _watchedCareer = Guid.TryParse(id, out var guid) ? guid : null;
        if (_watchedCareer is null) Closing();
        _lastPosted = null;
        return null;
    }

    /// <summary>Page errors go to page-errors.log next to the saves, for bug reports.</summary>
    private static object? Log(string message)
    {
        var file = Path.Combine(CareerStore.DefaultRoot, "page-errors.log");
        File.AppendAllText(file, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        return null;
    }

    /// <summary>A country the app knows (an ISO code from <see cref="Countries"/>), or null.</summary>
    private static string? Nationality(string code) =>
        Countries.Contains(code.Trim(), StringComparer.OrdinalIgnoreCase) ? code.Trim().ToUpperInvariant() : null;

    /// <summary>Every country Windows has a region for, as ISO codes; the page names them in the app's language.</summary>
    private static readonly IReadOnlyList<string> Countries = System.Globalization.CultureInfo.GetCultures(System.Globalization.CultureTypes.SpecificCultures)
        .Select(c => { try { return new System.Globalization.RegionInfo(c.Name).TwoLetterISORegionName; } catch (ArgumentException) { return ""; } })
        .Where(c => c.Length == 2 && char.IsLetter(c[0]) && char.IsLetter(c[1]))
        .Distinct().Order().ToList();

    /// <summary>The nationality a new career starts with: the Windows region, read on this PC only.</summary>
    private static string? DefaultNationality()
    {
        try { return Nationality(System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName); }
        catch (ArgumentException) { return null; }
    }

    private object Init() => new
    {
        countries = Countries,
        defaultNationality = DefaultNationality(),
        settings = _settings,
        lmu = LmuView(_settings.LmuRoot),
        detectedLmu = _install is null ? DetectLmu() : null,
        careers = _store.List(),
        features = new { aiDriverSwaps = Features.AiDriverSwaps },
        version = Updates.CurrentLabel,
        locale = LocaleView(),
    };

    private string Language => _locales.Resolve(_settings.Language, _settings.LmuRoot);

    /// <summary>The language to show and its translations, and the languages to choose from in Settings.</summary>
    private object LocaleView()
    {
        var language = Language;
        return new
        {
            language,
            choice = _settings.Language,
            lmuLanguage = Locales.SteamCode(_settings.LmuRoot),
            available = _locales.Available().Select(l => new { code = l.Code, name = l.Name }),
            strings = _locales.Strings(language),
            problems = _locales.Problems,
        };
    }

    /// <summary>
    /// Opens the folder players put translation files in, with a note on where to start the first
    /// time: the template and instructions are on GitHub.
    /// </summary>
    private static object? OpenLocales()
    {
        Directory.CreateDirectory(Locales.OwnFolder);
        var readme = Path.Combine(Locales.OwnFolder, "README.txt");
        if (!File.Exists(readme))
        {
            File.WriteAllText(readme,
                "Translations for Factory Seat. Each language is one file here, named for its code (de.json, pt-BR.json).\r\n" +
                "A file for a language the app already has only needs the strings you want to change.\r\n\r\n" +
                "The template with every string, and how to translate it:\r\n" +
                "https://github.com/factoryseatlmuapp/factory-seat/tree/main/src/LmuCareer.App/locales\r\n\r\n" +
                "Restart the app (or pick the language again in Settings) to see your changes.\r\n");
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{Locales.OwnFolder}\"") { UseShellExecute = true });
        return null;
    }

    /// <summary>No career is open any more (the player left it, or the app is closing): briefing.json says so.</summary>
    public static void Closing()
    {
        try { BriefingFile.MarkClosed(CareerStore.DefaultRoot); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Text the app shows itself, outside the page.</summary>
    private string T(string text) => _locales.Text(Language, text);

    private static object LmuView(string? root) => new
    {
        root,
        valid = root is not null && LmuInstall.LooksLikeInstall(root),
    };

    private static string? DetectLmu()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        var candidates = new[] { steam?.Replace('/', '\\'), @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
        return candidates.OfType<string>().Where(Directory.Exists).Select(LmuInstall.FindViaSteam).FirstOrDefault(r => r is not null);
    }

    private object BrowseLmu()
    {
        var dialog = new OpenFolderDialog { Title = T("Choose your Le Mans Ultimate folder") };
        if (dialog.ShowDialog(_window) != true) return new { path = (string?)null, valid = false };
        return new { path = dialog.FolderName, valid = LmuInstall.LooksLikeInstall(dialog.FolderName) };
    }

    private object SetLmuRoot(string path)
    {
        if (!LmuInstall.LooksLikeInstall(path))
            throw new PlayerError("That folder doesn't look like a Le Mans Ultimate install. It should contain Installed and UserData folders.");
        UseInstall(path);
        _settings.LmuRoot = path;
        _settings.Save();
        return LmuView(path);
    }

    private AppSettings SetPrefs(JsonElement args)
    {
        if (args.TryGetProperty("theme", out var theme)) _settings.Theme = theme.GetString() ?? "system";
        if (args.TryGetProperty("language", out var language)) _settings.Language = language.GetString() is { Length: > 0 } code ? code : "auto";
        if (args.TryGetProperty("soundVolume", out var volume)) _settings.SoundVolume = Math.Clamp(volume.GetDouble(), 0, 1);
        if (args.TryGetProperty("muted", out var muted)) _settings.Muted = muted.GetBoolean();
        if (args.TryGetProperty("checkForUpdates", out var updates)) _settings.CheckForUpdates = updates.GetBoolean();
        if (args.TryGetProperty("tourSeen", out var tour)) _settings.TourSeen = tour.ValueKind == JsonValueKind.String ? tour.GetString() : null;
        if (args.TryGetProperty("customSponsors", out var own))
            _settings.CustomSponsors = (JsonSerializer.Deserialize<List<OwnSponsor>>(own.GetRawText(), Json) ?? [])
                .Where(s => s.Id.StartsWith("own-", StringComparison.Ordinal) && s.Name.Trim().Length > 0)
                .Select(s => { s.Name = s.Name.Trim(); s.Region = s.Region.Trim(); return s; })
                .ToList();
        if (args.TryGetProperty("builtLiveries", out var built))
            _settings.BuiltLiveries = built.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).Distinct().ToList();
        _settings.Save();
        return _settings;
    }

    private object? SetTitleBar(bool dark)
    {
        _window.SetDarkTitleBar(dark);
        return null;
    }

    /// <summary>The catalog, with each track, layout and car marked as installed or not.</summary>
    private object CatalogView() => new
    {
        packs = _catalog.Packs,
        tracks = _catalog.Tracks.Select(t => new
        {
            t.Folder, t.Name, t.Location, t.Country, t.LengthKm, t.Pack,
            layouts = t.Layouts.Select(l => new { file = l.Key, name = l.Value, installed = _install?.HasLayout(t.Folder, l.Key) ?? true }),
        }),
        cars = _catalog.Cars.Select(c => new
        {
            c.Folder, c.Name, c.Class, c.Pack, c.CarTypes,
            installed = _install?.CarFolders.Contains(c.Folder, StringComparer.OrdinalIgnoreCase) ?? true,
        }),
        events = _catalog.Events,
        sponsors = _catalog.Sponsors,
    };

    private Round RoundFor(JsonElement args)
    {
        var carClass = Str(args, "carClass");
        int? minutes = args.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : null;

        if (args.TryGetProperty("eventId", out var eventId) && eventId.GetString() is { Length: > 0 } idValue)
        {
            var e = _catalog.Event(idValue) ?? throw new KeyNotFoundException($"No event \"{idValue}\".");
            return CalendarBuilder.RoundFor(_catalog, e, carClass, DriverRating.Silver, minutes);
        }

        // A custom event on any layout.
        var custom = args.GetProperty("custom");
        var customEvent = new EventInfo(
            Id: "",
            Name: Str(custom, "name"),
            Series: "custom",
            Month: custom.TryGetProperty("month", out var month) ? month.GetInt32() : 0,
            StartTime: custom.TryGetProperty("startTime", out var start) ? start.GetString() ?? "12:00" : "12:00",
            Hours: custom.GetProperty("hours").GetDouble(),
            Folder: Str(custom, "folder"),
            Layout: Str(custom, "layout"));
        if (customEvent.Name.Trim().Length == 0) throw new PlayerError("Give the custom event a name.");
        return CalendarBuilder.RoundFor(_catalog, customEvent, carClass, DriverRating.Silver, minutes);
    }

    private object CreateCareer(JsonElement args)
    {
        var carClass = Str(args, "carClass");
        var carFolder = Str(args, "carFolder");
        var car = _catalog.Cars.FirstOrDefault(c => c.Folder == carFolder) ?? throw new KeyNotFoundException($"No car \"{carFolder}\".");
        var name = Str(args, "name").Trim();
        if (name.Length == 0) throw new PlayerError("Give the career a name.");

        var rounds = args.GetProperty("rounds").EnumerateArray().Select(r => RoundFor(Merge(r, carClass))).ToList();

        var now = DateTimeOffset.Now;
        var career = new Career
        {
            Name = name,
            CreatedAt = now,
            LastPlayedAt = now,
            DriverName = Str(args, "driverName").Trim(),
            Nationality = Nationality(args.TryGetProperty("nationality", out var nationality) ? nationality.GetString() ?? "" : ""),
            ChampionshipOnly = args.TryGetProperty("championshipOnly", out var championshipOnly) && championshipOnly.GetBoolean(),
            GteClassic = carClass == "GTE" && args.TryGetProperty("gteClassic", out var gteClassic) && gteClassic.GetBoolean(),
            Path = args.TryGetProperty("path", out var path) ? path.GetString() switch
            {
                "gt" => CareerPath.Gt,
                "prototype" => CareerPath.Prototype,
                _ => null,
            } : null,
            OwnedContent = Strings(args, "ownedPacks").ToList(),
            CurrentSeason = new Season
            {
                Number = 1,
                Car = new CareerCar(car.CarTypes.FirstOrDefault() ?? "", carClass, Str(args, "carNumber").Trim(), Str(args, "teamName").Trim())
                {
                    Folder = car.Folder,
                    OtherCarTypes = car.CarTypes.Skip(1).ToList(),
                },
                Contract = new Contract
                {
                    TeamName = Str(args, "teamName").Trim(),
                    Tier = 2,
                    TargetPosition = Progression.DefaultTarget,
                },
                Rounds = CalendarBuilder.Number(rounds),
            },
        };
        if (career.CurrentSeason.Rounds.Count == 0) throw new PlayerError("The season needs at least one round.");

        _store.Save(career);
        return new { id = career.Id };
    }

    /// <summary>A round spec from the season builder, with the career's class added.</summary>
    private static JsonElement Merge(JsonElement round, string carClass)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(round.GetRawText()) ?? [];
        dict["carClass"] = JsonSerializer.SerializeToElement(carClass);
        return JsonSerializer.SerializeToElement(dict);
    }

    private Career Load(JsonElement args)
    {
        var loaded = _store.Load(Guid.Parse(Str(args, "id")));
        return loaded.Career;
    }

    /// <summary>A counted round's lap analysis: the debrief's figures, the player's laps and the AI's class bests.</summary>
    private object? RoundAnalysis(JsonElement args)
    {
        var career = Load(args);
        var seasonNumber = args.GetProperty("season").GetInt32();
        var roundNumber = args.GetProperty("round").GetInt32();
        var round = career.PastSeasons.Append(career.CurrentSeason).FirstOrDefault(s => s.Number == seasonNumber)?
            .Rounds.FirstOrDefault(r => r.Number == roundNumber)
            ?? throw new KeyNotFoundException("No such round.");
        var me = round.Result?.Entries.FirstOrDefault(e => e.Entry.IsPlayer)?.Entry;
        if (me is null) return null;
        return new
        {
            eventName = round.EventName,
            report = DebriefReport.For(round),
            laps = me.LapRecords,
            classBest = round.Result!.Debrief?.ClassBests.FirstOrDefault(b => b.CarClass == me.CarClass),
        };
    }

    /// <summary>A finished season's final standings, for the Career tab's season list.</summary>
    private object SeasonStandings(JsonElement args)
    {
        var career = Load(args);
        var seasonNumber = args.GetProperty("season").GetInt32();
        var season = career.PastSeasons.Append(career.CurrentSeason).FirstOrDefault(s => s.Number == seasonNumber)
            ?? throw new KeyNotFoundException("No such season.");
        return new
        {
            season = season.Number,
            carClass = season.Car.CarClass,
            teamName = season.Car.TeamName,
            rounds = season.ChampionshipRounds.Where(r => r.State == RoundState.Completed)
                .Select(r => new { number = r.Number, eventName = r.EventName }),
            standings = season.Standings(),
        };
    }

    /// <summary>Marks a message read (or all, with no messageId). Reading mail isn't playing: the career keeps its place in the list.</summary>
    private object ReadMessage(JsonElement args)
    {
        var career = Load(args);
        Inbox.MarkRead(career, args.TryGetProperty("messageId", out var message) ? message.GetString() : null);
        _store.Save(career);
        return CareerViewData(career);
    }

    private object Change(JsonElement args, Action<Career> change)
    {
        var career = Load(args);
        change(career);
        career.LastPlayedAt = DateTimeOffset.Now;
        _store.Save(career);
        return CareerView(career);
    }

    /// <summary>Opens a career, making the season's sponsor and guest drive offers the first time it's opened before the season starts.</summary>
    private object GetCareer(JsonElement args)
    {
        var career = Load(args);
        var sponsors = Sponsorship.EnsureOffers(career, _catalog, _settings.BuiltLiveries,
            _settings.CustomSponsors.Select(s => s.ToSponsor()).ToList(), language: Language);
        var guests = GuestDrives.EnsureOffers(career, _catalog, _install);
        guests |= TestDays.EnsureOffers(career, _catalog, _install);
        var customTeam = CareerActions.RecognizeCustomTeam(career);
        var phrases = CareerActions.AddPhrases(career);
        var debriefs = Debriefs.Backfill(career, _install?.ResultsFolder);
        var inbox = Inbox.Start(career, DateTimeOffset.Now);
        if (sponsors || guests || customTeam || phrases || debriefs || inbox) _store.Save(career);
        return CareerView(career);
    }

    private object CareerView(Career career)
    {
        WriteBriefing(career);
        return CareerViewData(career);
    }

    /// <summary>
    /// Keeps briefing.json next to the saves up to date with the career on screen, for companion
    /// apps. A file that can't be written (locked by a reader) waits for the next change.
    /// </summary>
    private static void WriteBriefing(Career career)
    {
        try
        {
            BriefingFile.For(career, ContentCatalog.Default, Updates.CurrentLabel, DateTimeOffset.Now)
                .Write(CareerStore.DefaultRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"briefing.json not written: {ex.Message}");
        }
    }

    private object CareerViewData(Career career) => new
    {
        career,
        sponsors = career.CurrentSeason.Sponsors.Select(d => new
        {
            deal = d,
            text = Sponsorship.Describe(d),
            progress = Sponsorship.Progress(career.CurrentSeason, d),
        }),
        sponsorOffers = (career.CurrentSeason.SponsorOffers ?? []).Select(d => new { deal = d, text = Sponsorship.Describe(d) }),
        canSignSponsors = !career.ChampionshipOnly && Sponsorship.CanSign(career.CurrentSeason),
        pace = PaceCheck.Summarize(career.CurrentSeason),
        roundPace = career.CurrentSeason.Rounds.Select(PaceCheck.For).OfType<RoundPace>(),
        liveryRounds = Sponsorship.LiveryRounds(career.CurrentSeason) is var (custom, counted) ? new { custom, counted } : null,
        standings = career.CurrentSeason.Standings(),
        // Standings of each championship raced beside the season's own (IMSA), by id.
        seriesStandings = career.CurrentSeason.Series.ToDictionary(s => s.Id, s => career.CurrentSeason.Standings(s.Id)),
        // Loyalty with each make and team, strongest first; how the current team is doing.
        loyalty = career.Loyalty
            .Select(l => new
            {
                l.Kind, l.Id, l.Points, l.Burned, l.Left, l.LastSeason,
                name = Loyalties.Name(career, l, _catalog),
                stage = Loyalties.Stage(l.Points),
                hypercar = Loyalties.HasHypercar(l, _catalog),
            })
            .OrderByDescending(l => l.Points),
        // For each offer on the table: what signing it does to the loyalty left behind, and the
        // standing the player already has with the make or team it's with.
        offerNotes = career.Offers.Select(o => new
        {
            o.Id,
            leaving = career.CurrentSeason.Review is null ? null
                : Loyalties.Preview(career, career.CurrentSeason, o, _catalog, o.Kind == OfferKind.Promotion) is { } p
                    ? new { name = Loyalties.Name(career, p.From, _catalog), p.Departure, before = p.From.Points, after = p.PointsAfter, burns = p.Burns }
                    : null,
            joining = Loyalties.Key(o.CarClass, o.CarFolder, o.TeamName, _catalog) is { } key && Loyalties.Find(career, key.Kind, key.Id) is { } with
                && (Loyalties.Key(career.CurrentSeason, _catalog) != key)
                ? new { name = Loyalties.Name(career, with, _catalog), stage = Loyalties.Stage(with.Points), with.Burned }
                : null,
            rival = Rivals.IsRivalTeam(career, o.TeamName),
        }),
        teamStatus = career.ChampionshipOnly ? (TeamStatus?)null
            : Teams.Status(career.CurrentSeason, career.CurrentSeason.Contract?.TeamName is { Length: > 0 } team ? team : career.CurrentSeason.Car.TeamName,
                career.CurrentSeason.Car.CarClass, career.CurrentSeason.Contract?.Tier ?? 2),
        carName = _catalog.CarByType(career.CurrentSeason.Car.CarType)?.Name ?? career.CurrentSeason.Car.CarType,
        resultsFolder = _install?.ResultsFolder,
        ladder = LadderView(career),
        unread = Inbox.Unread(career),
        // The next race's stakes, history here, practice and AI Strength advice.
        preparation = career.CurrentSeason.NextRound is { } next ? Preparation.For(career, next) : null,
        // The season review worked out now, until the real one is written.
        projectedReputation = !career.ChampionshipOnly && career.CurrentSeason.Review is null && career.CurrentSeason.ChampionshipRounds.Any(r => r.State == RoundState.Completed)
            ? Progression.Review(career.CurrentSeason, career.Reputation) is var review ? review.ReputationAfter - review.ReputationBefore : (decimal?)null
            : null,
        // The numbers behind each debrief in the inbox, by "season-round".
        debriefs = career.PastSeasons.Append(career.CurrentSeason)
            .SelectMany(s => s.Rounds.Select(r => (Season: s, Round: r)))
            .Where(x => career.Inbox.Any(m => m.Kind == MessageKind.Debrief && m.Season == x.Season.Number && m.Round == x.Round.Number))
            .Select(x => (Key: $"{x.Season.Number}-{x.Round.Number}", Report: DebriefReport.For(x.Round)))
            .Where(x => x.Report is not null)
            .ToDictionary(x => x.Key, x => x.Report),
    };

    /// <summary>What the next step up the ladder asks for, for the off-season and career screens.</summary>
    private object LadderView(Career career)
    {
        var next = career.GteClassic ? null : Progression.NextClass(career.CurrentSeason.Car.CarClass,
            carClass => _catalog.Cars.Any(c => c.Class == carClass && Progression.CanOffer(c, career, _install)), career.Path);
        return new
        {
            next,
            threshold = next is null ? (decimal?)null : Progression.Threshold(next),
            prototypeSeasons = Progression.StrongPrototypeSeasons(career),
            prototypeSeasonsNeeded = Progression.PrototypeSeasonsForHypercar,
        };
    }

    /// <summary>Reviews the finished season and makes the offers for the next, the first time it's asked.</summary>
    private object EndSeason(JsonElement args)
    {
        var career = Load(args);
        if (Progression.EndSeason(career, _catalog, _install)) _store.Save(career);
        return CareerView(career);
    }

    /// <summary>Signs an offer and starts the next season on the calendar from the season builder.</summary>
    private object StartNextSeason(JsonElement args)
    {
        var career = Load(args);
        var offer = career.Offers.FirstOrDefault(o => o.Id == Str(args, "offerId"))
            ?? throw new KeyNotFoundException("That offer isn't on the table any more.");
        var rounds = args.GetProperty("rounds").EnumerateArray().Select(r => RoundFor(Merge(r, offer.CarClass))).ToList();

        Progression.StartNextSeason(career, offer.Id, rounds, _catalog);
        career.LastPlayedAt = DateTimeOffset.Now;
        _store.Save(career);
        return new { id = career.Id };
    }

    /// <summary>A championship-only career's next season: any car, on the calendar from the season builder.</summary>
    private object StartFreeSeason(JsonElement args)
    {
        var career = Load(args);
        var car = _catalog.Cars.FirstOrDefault(c => c.Folder == Str(args, "carFolder"))
            ?? throw new KeyNotFoundException($"No car \"{Str(args, "carFolder")}\".");
        var rounds = args.GetProperty("rounds").EnumerateArray().Select(r => RoundFor(Merge(r, car.Class))).ToList();

        Progression.StartFreeSeason(career, car, Str(args, "carNumber"), Str(args, "teamName"), rounds);
        career.LastPlayedAt = DateTimeOffset.Now;
        _store.Save(career);
        return new { id = career.Id };
    }

    private object? DeleteCareer(JsonElement args)
    {
        var id = Guid.Parse(Str(args, "id"));
        _store.Delete(id);
        try { BriefingFile.RemoveFor(CareerStore.DefaultRoot, id); } catch (IOException) { }
        return null;
    }

    private object ExportCareer(JsonElement args)
    {
        var career = Load(args);
        var dialog = new SaveFileDialog
        {
            Title = T("Export career"),
            FileName = string.Concat(career.Name.Split(Path.GetInvalidFileNameChars())) + ".career.json",
            Filter = T("Career save") + " (*.career.json)|*.career.json",
        };
        if (dialog.ShowDialog(_window) != true) return new { exported = false };
        _store.Export(career.Id, dialog.FileName);
        return new { exported = true };
    }

    private object ImportCareer()
    {
        var dialog = new OpenFileDialog
        {
            Title = T("Import career"),
            Filter = T("Career save") + " (*.career.json)|*.career.json|" + T("All files") + "|*.*",
        };
        if (dialog.ShowDialog(_window) != true) return new { imported = false };
        var career = _store.Import(dialog.FileName);
        return new { imported = true, id = career.Id };
    }

    private object AcceptRound(JsonElement args)
    {
        var career = Load(args);
        var evaluation = CareerActions.CheckArmedRound(_store, career, ResultsFolder, _install?.RaceSavesFolder);
        var round = career.CurrentSeason.ArmedRound;

        // A test day is judged on its practice laps, which the check above has just read.
        if (round?.Test is not null)
        {
            TestDays.Complete(career, DateTimeOffset.Now);
            _store.Save(career);
            return new { view = CareerView(career), noticed = (TeamInterest?)null };
        }

        CareerActions.AcceptArmedRound(_store, career, evaluation,
            takeDnf: args.TryGetProperty("takeDnf", out var dnf) && dnf.GetBoolean(),
            acceptDifferences: args.TryGetProperty("acceptDifferences", out var despite) && despite.GetBoolean(),
            DateTimeOffset.Now);

        // A big win can get the player noticed by a better team, and a result above expectations by a sponsor.
        var noticed = round is null ? null : Progression.NoticeResult(career, round, _catalog, _install);
        var approach = round is null ? null : Sponsorship.Approach(career, round, _catalog, _settings.BuiltLiveries,
            _settings.CustomSponsors.Select(s => s.ToSponsor()).ToList(), Language, DateTimeOffset.Now);
        if (noticed is not null || approach is not null) _store.Save(career);
        return new { view = CareerView(career), noticed };
    }

    private static object EvaluationView(RoundEvaluation evaluation) => new
    {
        status = evaluation.Status,
        savedAs = evaluation.SavedAs,
        practiceChanged = evaluation.PracticeChanged,
        race = CheckView(evaluation.Race),
        qualifying = CheckView(evaluation.Qualifying),
        ignored = evaluation.Ignored.Select(CheckView),
    };

    private static object? CheckView(SessionCheck? check) => check is null ? null : new
    {
        file = check.FileName,
        writtenAt = check.WrittenAt,
        kind = check.Session.Kind,
        verdict = check.Verdict,
        reasons = check.Reasons,
        wrongCar = check.WrongCar,
        track = check.Session.TrackCourse,
        complete = check.Session.IsComplete,
        player = check.Session.Player is { } p ? new { p.Name, p.CarType, p.CarNumber, p.ClassPosition, p.Status, p.Laps } : null,
    };

    private static string Str(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static IReadOnlyCollection<string> Strings(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];
}
