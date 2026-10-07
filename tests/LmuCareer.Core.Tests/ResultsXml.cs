using System.Globalization;
using System.Security;
using System.Xml.Linq;
using LmuCareer.Core.Results;

namespace LmuCareer.Core.Tests;

/// <summary>Builds results XML in the shape LMU writes it, with only the fields the tests need.</summary>
internal sealed class ResultsXml
{
    private readonly List<string> _drivers = [];
    private readonly List<string> _stream = [];

    public string Session { get; init; } = "Race";
    public string Setting { get; init; } = "Race Weekend";
    public string Course { get; init; } = "Circuit de la Sarthe";
    public long DateTime { get; init; } = 1_790_000_000;
    public int RaceMinutes { get; init; } = 30;
    public double Fuel { get; init; } = 3.5;
    public double Tires { get; init; } = 3.5;
    public string TrackData { get; init; } = "";

    public ResultsXml Car(
        string name,
        string carClass = "GT3",
        string carType = "Lexus RCF LMGT3",
        int classPos = 1,
        int laps = 10,
        string status = "Finished Normally",
        string team = "Team",
        string number = "1",
        bool player = false,
        string vehicle = "",
        params (int Start, int End, string Controller)[] control)
    {
        if (control.Length == 0) control = [(1, laps, player ? "PlayerControl,TC=2" : "AIControl")];
        var controlXml = string.Concat(control.Select(c =>
            $"<ControlAndAids startLap=\"{c.Start}\" endLap=\"{c.End}\">{c.Controller}</ControlAndAids>"));

        _drivers.Add($"""
            <Driver>
              <Name>{SecurityElement.Escape(name)}</Name>
              <TeamName>{SecurityElement.Escape(team)}</TeamName>
              <CarNumber>{number}</CarNumber>
              <VehName>{SecurityElement.Escape(vehicle)}</VehName>
              <CarType>{SecurityElement.Escape(carType)}</CarType>
              <CarClass>{carClass}</CarClass>
              <isPlayer>{(player ? 1 : 0)}</isPlayer>
              <Position>{classPos}</Position>
              <ClassPosition>{classPos}</ClassPosition>
              <Laps>{laps}</Laps>
              <BestLapTime>{100 + classPos}.5</BestLapTime>
              <Pitstops>1</Pitstops>
              <FinishStatus>{status}</FinishStatus>
              {controlXml}
              <Lap num="1" p="{classPos}" et="120.5">--.----</Lap>
              <Lap num="2" p="{classPos}" et="221.0">100.5</Lap>
            </Driver>
            """);
        return this;
    }

    public ResultsXml Event(string xml)
    {
        _stream.Add(xml);
        return this;
    }

    public XDocument Build() => XDocument.Parse($"""
        <rFactorXML version="1.0">
          <RaceResults>
            <Setting>{Setting}</Setting>
            <DateTime>{DateTime}</DateTime>
            <TrackVenue>{Course}</TrackVenue>
            <TrackCourse>{Course}</TrackCourse>
            <TrackData>{SecurityElement.Escape(TrackData)}</TrackData>
            <TrackLength>13626.0</TrackLength>
            <GameVersion>1.4200</GameVersion>
            <RaceTime>{RaceMinutes}</RaceTime>
            <FuelMult>{Fuel.ToString(CultureInfo.InvariantCulture)}</FuelMult>
            <TireMult>{Tires.ToString(CultureInfo.InvariantCulture)}</TireMult>
            <{Session}>
              <DateTime>{DateTime.ToString(CultureInfo.InvariantCulture)}</DateTime>
              <MostLapsCompleted>10</MostLapsCompleted>
              <Stream>{string.Concat(_stream)}</Stream>
              {string.Concat(_drivers)}
            </{Session}>
          </RaceResults>
        </rFactorXML>
        """);

    public SessionResult Parse(string? fileName = null) => ResultsParser.Parse(Build(), fileName ?? $"{Session}.xml");
}
