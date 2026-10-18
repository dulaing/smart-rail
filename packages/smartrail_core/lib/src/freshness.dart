import 'generated/status.g.dart';

/// How old simulated running evidence is. A pure rule: it never fetches, polls or stores anything.
/// Its TypeScript twin is shared/design/freshness.ts; both run
/// tests/SmartRail.Api.Tests/Rail/freshness-vectors.json.
///
/// [now] must already be corrected with server time. Age is measured from [observedAt], not from
/// the last HTTP response, so a 304 leaves the answer unchanged.
SrEvidence freshnessOf({
  required DateTime? observedAt,
  required DateTime now,
  int ageingAfterSeconds = srFreshnessFallbackAgeingSeconds,
  int unknownAfterSeconds = srFreshnessFallbackUnknownSeconds,
}) {
  if (observedAt == null) return SrEvidence.unknown;

  // An observation slightly "in the future" after clock correction is treated as just made.
  var age = now.difference(observedAt);
  if (age.isNegative) age = Duration.zero;

  if (age < Duration(seconds: ageingAfterSeconds)) return SrEvidence.current;
  if (age < Duration(seconds: unknownAfterSeconds)) return SrEvidence.ageing;
  return SrEvidence.unknown;
}

/// How far the device clock is behind (positive) or ahead of (negative) the server, from the
/// `X-SmartRail-Server-Time` header and the device time when that response arrived.
Duration clockOffset({required DateTime serverTime, required DateTime deviceTimeAtResponse}) =>
    serverTime.difference(deviceTimeAtResponse);
