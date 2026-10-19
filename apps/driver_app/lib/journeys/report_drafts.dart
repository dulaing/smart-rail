import 'dart:convert';
import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'briefing_api.dart';

/// A report whose delivery is not confirmed: the one key, reason and journey it was written for.
///
/// It keeps a copy of the journey's details so the Reports list can still reach it after the
/// service date changes, when today's assigned journeys no longer include that journey.
class ReportDraft {
  const ReportDraft({
    required this.ownerId,
    required this.journeyId,
    required this.clientReportId,
    required this.reasonCode,
    this.journey,
  });

  final String ownerId;
  final String journeyId;
  final String clientReportId;
  final String reasonCode;

  /// The journey as it was when the report was written. Null for a draft saved by an older
  /// version of the app, which did not keep it.
  final DriverJourney? journey;

  Map<String, dynamic> toJson() => {
    'ownerId': ownerId,
    'journeyId': journeyId,
    'clientReportId': clientReportId,
    'reasonCode': reasonCode,
    'journey': journey?.toJson(),
  };

  /// Null when the saved row is damaged or is not this owner's.
  static ReportDraft? fromJson(Object? raw, {String? ownerId}) {
    try {
      final json = Map<String, dynamic>.from(raw as Map);
      if (ownerId != null && json['ownerId'] != ownerId) return null;
      final saved = json['journey'];
      return ReportDraft(
        ownerId: json['ownerId'] as String,
        journeyId: json['journeyId'] as String,
        clientReportId: json['clientReportId'] as String,
        reasonCode: json['reasonCode'] as String,
        journey: saved == null
            ? null
            : DriverJourney.fromJson(Map<String, dynamic>.from(saved as Map)),
      );
    } catch (_) {
      return null;
    }
  }
}

/// Every unconfirmed report this owner has, oldest service date first, from the saved rows.
/// Rows of another owner or in a damaged state are left out.
List<ReportDraft> pendingDrafts(Map<String, dynamic> saved, String ownerId) {
  final drafts = [
    for (final raw in saved.values) ?ReportDraft.fromJson(raw, ownerId: ownerId),
  ];
  // A draft with no saved journey has no date to sort by, so it comes first.
  int order(ReportDraft a, ReportDraft b) {
    final left = a.journey?.serviceDate;
    final right = b.journey?.serviceDate;
    if (left == null || right == null) {
      return left == right ? a.journeyId.compareTo(b.journeyId) : (left == null ? -1 : 1);
    }
    final byDate = left.compareTo(right);
    return byDate != 0 ? byDate : a.journeyId.compareTo(b.journeyId);
  }

  return drafts..sort(order);
}

class ReportDraftStore {
  ReportDraftStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();
  final FlutterSecureStorage _storage;

  static const _key = 'driver_report_drafts';

  Future<Map<String, dynamic>> _all() async {
    final raw = await _storage.read(key: _key);
    if (raw == null) return {};
    try {
      return Map<String, dynamic>.from(jsonDecode(raw) as Map);
    } catch (_) {
      return {};
    }
  }

  Future<ReportDraft?> read(String ownerId, String journeyId) async {
    final draft = ReportDraft.fromJson(
      (await _all())[journeyId],
      ownerId: ownerId,
    );
    return draft?.journeyId == journeyId ? draft : null;
  }

  /// All of this owner's unconfirmed reports, whatever service date they were written for.
  Future<List<ReportDraft>> pendingFor(String ownerId) async =>
      pendingDrafts(await _all(), ownerId);

  Future<ReportDraft> create(
    String ownerId,
    DriverJourney journey,
    String reasonCode,
  ) async {
    final draft = ReportDraft(
      ownerId: ownerId,
      journeyId: journey.id,
      clientReportId: _uuidV4(),
      reasonCode: reasonCode,
      journey: journey,
    );
    final all = await _all();
    all[journey.id] = draft.toJson();
    await _storage.write(key: _key, value: jsonEncode(all));
    return draft;
  }

  Future<void> clear(String journeyId) async {
    final all = await _all()
      ..remove(journeyId);
    if (all.isEmpty) {
      await _storage.delete(key: _key);
    } else {
      await _storage.write(key: _key, value: jsonEncode(all));
    }
  }

  Future<void> clearAll() => _storage.delete(key: _key);
}

String _uuidV4() {
  final random = Random.secure();
  final bytes = List<int>.generate(16, (_) => random.nextInt(256));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  final hex = bytes.map((b) => b.toRadixString(16).padLeft(2, '0')).join();
  return '${hex.substring(0, 8)}-${hex.substring(8, 12)}-${hex.substring(12, 16)}-${hex.substring(16, 20)}-${hex.substring(20)}';
}
