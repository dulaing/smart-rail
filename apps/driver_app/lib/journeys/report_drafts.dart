import 'dart:convert';
import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

class ReportDraft {
  const ReportDraft({
    required this.ownerId,
    required this.journeyId,
    required this.clientReportId,
    required this.reasonCode,
  });
  final String ownerId;
  final String journeyId;
  final String clientReportId;
  final String reasonCode;
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
    final raw = (await _all())[journeyId];
    if (raw == null) return null;
    try {
      final json = Map<String, dynamic>.from(raw as Map);
      if (json['ownerId'] != ownerId || json['journeyId'] != journeyId) {
        return null;
      }
      return ReportDraft(
        ownerId: ownerId,
        journeyId: journeyId,
        clientReportId: json['clientReportId'] as String,
        reasonCode: json['reasonCode'] as String,
      );
    } catch (_) {
      return null;
    }
  }

  Future<ReportDraft> create(
    String ownerId,
    String journeyId,
    String reasonCode,
  ) async {
    final draft = ReportDraft(
      ownerId: ownerId,
      journeyId: journeyId,
      clientReportId: _uuidV4(),
      reasonCode: reasonCode,
    );
    final all = await _all();
    all[journeyId] = {
      'ownerId': ownerId,
      'journeyId': journeyId,
      'clientReportId': draft.clientReportId,
      'reasonCode': reasonCode,
    };
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
