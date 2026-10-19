import 'dart:convert';

class PassengerStation {
  const PassengerStation({
    required this.id,
    required this.code,
    required this.nameEn,
    required this.nameSi,
    required this.nameTa,
  });
  final String id, code, nameEn, nameSi, nameTa;
  factory PassengerStation.fromJson(Map<String, dynamic> j) => PassengerStation(
    id: j['id'] as String,
    code: j['code'] as String,
    nameEn: j['nameEn'] as String,
    nameSi: j['nameSi'] as String,
    nameTa: j['nameTa'] as String,
  );
  String name(String locale) => switch (locale) {
    'si' => nameSi,
    'ta' => nameTa,
    _ => nameEn,
  };
  Map<String, dynamic> toJson() => {
    'id': id,
    'code': code,
    'nameEn': nameEn,
    'nameSi': nameSi,
    'nameTa': nameTa,
  };
}

class PassengerStop {
  const PassengerStop({
    required this.id,
    required this.stationId,
    required this.stationCode,
    required this.sequence,
    this.arrival,
    this.departure,
    this.platform,
  });
  final String id, stationId, stationCode;
  final int sequence;
  final DateTime? arrival, departure;
  final String? platform;
  factory PassengerStop.fromJson(Map<String, dynamic> j) => PassengerStop(
    id: j['stopId'] as String,
    stationId: j['stationId'] as String,
    stationCode: j['stationCode'] as String,
    sequence: j['sequence'] as int,
    arrival: _date(j['scheduledArrival']),
    departure: _date(j['scheduledDeparture']),
    platform: j['plannedPlatform'] as String?,
  );
  Map<String, dynamic> toJson() => {
    'stopId': id,
    'stationId': stationId,
    'stationCode': stationCode,
    'sequence': sequence,
    'scheduledArrival': arrival?.toIso8601String(),
    'scheduledDeparture': departure?.toIso8601String(),
    'plannedPlatform': platform,
  };
}

class PassengerJourney {
  const PassengerJourney({
    required this.id,
    required this.serviceDate,
    required this.trainNumber,
    required this.sourceLabel,
    required this.stops,
  });
  final String id, trainNumber, sourceLabel;
  final DateTime serviceDate;
  final List<PassengerStop> stops;
  factory PassengerJourney.fromJson(Map<String, dynamic> j) => PassengerJourney(
    id: j['journeyId'] as String,
    serviceDate: DateTime.parse(j['serviceDate'] as String),
    trainNumber: j['trainNumber'] as String,
    sourceLabel: j['sourceLabel'] as String,
    stops: (j['stops'] as List)
        .map((s) => PassengerStop.fromJson(s as Map<String, dynamic>))
        .toList(),
  );
  Map<String, dynamic> toJson() => {
    'journeyId': id,
    'serviceDate': _day(serviceDate),
    'trainNumber': trainNumber,
    'sourceLabel': sourceLabel,
    'stops': stops.map((s) => s.toJson()).toList(),
  };
}

class PassengerLiveTrain {
  const PassengerLiveTrain({
    required this.journeyId,
    required this.serviceDate,
    required this.trainNumber,
    required this.originStopId,
    required this.destinationStopId,
    this.scheduledOriginArrival,
    required this.scheduledDeparture,
    required this.scheduledArrival,
    required this.freshness,
    required this.overdue,
    required this.availability,
    this.estimatedDeparture,
    this.estimatedArrival,
    this.lastObservedAt,
    this.plannedPlatform,
    this.confirmedPlatform,
    this.confirmedPlatformAt,
    this.finalStopId,
  });
  final String journeyId,
      trainNumber,
      originStopId,
      destinationStopId,
      freshness,
      availability;
  final DateTime serviceDate, scheduledDeparture, scheduledArrival;
  final DateTime? scheduledOriginArrival;
  final DateTime? estimatedDeparture,
      estimatedArrival,
      lastObservedAt,
      confirmedPlatformAt;
  final String? plannedPlatform, confirmedPlatform, finalStopId;
  final bool overdue;
  factory PassengerLiveTrain.fromJson(Map<String, dynamic> j) =>
      PassengerLiveTrain(
        journeyId: j['journeyId'],
        serviceDate: DateTime.parse(j['serviceDate']),
        trainNumber: j['trainNumber'],
        originStopId: j['originStopId'],
        destinationStopId: j['destinationStopId'],
        scheduledOriginArrival: _date(j['originScheduledArrival']),
        scheduledDeparture: DateTime.parse(j['originScheduledDeparture']),
        scheduledArrival: DateTime.parse(j['destinationScheduledArrival']),
        estimatedDeparture: _date(j['originEstimatedDeparture']),
        estimatedArrival: _date(j['destinationEstimatedArrival']),
        freshness: j['freshness'],
        overdue: j['overdue'],
        availability: j['availability'],
        lastObservedAt: _date(j['lastObservedAt']),
        plannedPlatform: j['plannedPlatform'],
        confirmedPlatform: j['confirmedPlatform'],
        confirmedPlatformAt: _date(j['confirmedPlatformAt']),
        finalStopId: j['finalStopId'],
      );
}

class PassengerNotice {
  const PassengerNotice({
    required this.journeyId,
    required this.serviceDate,
    required this.trainNumber,
    required this.kind,
    required this.originStopId,
    required this.destinationStopId,
    required this.scheduledDeparture,
    this.finalStopId,
    this.confirmedAt,
  });
  final String journeyId, trainNumber, kind, originStopId, destinationStopId;
  final DateTime serviceDate, scheduledDeparture;
  final String? finalStopId;
  final DateTime? confirmedAt;
  factory PassengerNotice.fromJson(Map<String, dynamic> j) => PassengerNotice(
    journeyId: j['journeyId'],
    serviceDate: DateTime.parse(j['serviceDate']),
    trainNumber: j['trainNumber'],
    kind: j['kind'],
    originStopId: j['originStopId'],
    destinationStopId: j['destinationStopId'],
    scheduledDeparture: DateTime.parse(j['originScheduledDeparture']),
    finalStopId: j['finalStopId'],
    confirmedAt: _date(j['confirmedAt']),
  );
  Map<String, dynamic> toJson() => {
    'journeyId': journeyId,
    'serviceDate': _day(serviceDate),
    'trainNumber': trainNumber,
    'kind': kind,
    'originStopId': originStopId,
    'destinationStopId': destinationStopId,
    'originScheduledDeparture': scheduledDeparture.toIso8601String(),
    'finalStopId': finalStopId,
    'confirmedAt': confirmedAt?.toIso8601String(),
  };
}

class RouteSnapshot {
  const RouteSnapshot({
    required this.originId,
    required this.destinationId,
    required this.trains,
    required this.notices,
    required this.observedAt,
    required this.downloadedAt,
  });
  final String originId, destinationId;
  final List<PassengerLiveTrain> trains;
  final List<PassengerNotice> notices;
  final DateTime observedAt, downloadedAt;
  factory RouteSnapshot.fromJson(
    Map<String, dynamic> j,
    DateTime downloadedAt,
  ) => RouteSnapshot(
    originId: j['originId'],
    destinationId: j['destinationId'],
    trains: (j['trains'] as List)
        .map((t) => PassengerLiveTrain.fromJson(t))
        .toList(),
    notices: (j['notices'] as List)
        .map((n) => PassengerNotice.fromJson(n))
        .toList(),
    observedAt: DateTime.parse(j['serverTime'] as String),
    downloadedAt: downloadedAt,
  );
}

DateTime? _date(dynamic value) =>
    value == null ? null : DateTime.parse(value as String);
String _day(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
String encodeJson(Object value) => jsonEncode(value);
