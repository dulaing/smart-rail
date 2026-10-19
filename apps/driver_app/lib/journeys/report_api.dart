import 'dart:convert';

import 'package:http/http.dart' as http;

import '../auth/auth_service.dart';
import '../platform_client.dart';

class DriverReport {
  const DriverReport({
    required this.id,
    required this.clientReportId,
    required this.journeyId,
    required this.reasonCode,
    required this.receivedAt,
    required this.state,
    required this.acknowledgedByName,
    required this.acknowledgedAt,
  });

  factory DriverReport.fromJson(Map<String, dynamic> json) => DriverReport(
    id: json['reportId'] as String,
    clientReportId: json['clientReportId'] as String,
    journeyId: json['journeyId'] as String,
    reasonCode: json['reasonCode'] as String,
    receivedAt: DateTime.parse(json['receivedAt'] as String),
    state: json['state'] as String,
    acknowledgedByName: json['acknowledgedByName'] as String?,
    acknowledgedAt: json['acknowledgedAt'] == null
        ? null
        : DateTime.parse(json['acknowledgedAt'] as String),
  );

  final String id;
  final String clientReportId;
  final String journeyId;
  final String reasonCode;
  final DateTime receivedAt;
  final String state;
  final String? acknowledgedByName;
  final DateTime? acknowledgedAt;
}

sealed class ReportResult {
  const ReportResult();
}

class ReportOk extends ReportResult {
  const ReportOk(this.report);
  final DriverReport report;
}

class ReportListOk extends ReportResult {
  const ReportListOk(this.reports);
  final List<DriverReport> reports;
}

class ReportFailure extends ReportResult {
  const ReportFailure(this.status, this.message, {this.code});
  final int? status;
  final String message;
  final String? code;
}

class DriverReportApi {
  DriverReportApi(this._baseUrl, this._auth, {http.Client? client})
    : _client = client ?? platformHttpClient();

  final String _baseUrl;
  final AuthService _auth;
  final http.Client _client;

  Future<ReportResult> submit(
    String journeyId,
    String clientReportId,
    String reasonCode,
  ) => _send(
    'POST',
    journeyId,
    clientReportId: clientReportId,
    reasonCode: reasonCode,
  );

  Future<ReportResult> findByClientId(
    String journeyId,
    String clientReportId,
  ) => _send('GET', journeyId, clientReportId: clientReportId);

  Future<ReportResult> list(String journeyId) => _send('GET', journeyId);

  Future<ReportResult> _send(
    String method,
    String journeyId, {
    String? clientReportId,
    String? reasonCode,
  }) async {
    try {
      final token = await _auth.accessToken();
      final base = Uri.parse(
        '$_baseUrl/api/driver/journeys/$journeyId/reports',
      );
      final uri = method == 'GET' && clientReportId != null
          ? base.replace(queryParameters: {'clientReportId': clientReportId})
          : base;
      final headers = <String, String>{'Authorization': 'Bearer $token'};
      if (method == 'POST') headers['Content-Type'] = 'application/json';
      final response = method == 'GET'
          ? await _client.get(uri, headers: headers)
          : await _client.post(
              uri,
              headers: headers,
              body: jsonEncode({
                'clientReportId': clientReportId,
                'reasonCode': reasonCode,
              }),
            );
      if (response.statusCode == 200 || response.statusCode == 201) {
        final json = jsonDecode(response.body) as Map<String, dynamic>;
        if (method == 'GET') {
          return ReportListOk([
            for (final row in json['items'] as List)
              DriverReport.fromJson(row as Map<String, dynamic>),
          ]);
        }
        return ReportOk(DriverReport.fromJson(json));
      }
      final problem = jsonDecode(response.body) as Map<String, dynamic>;
      return ReportFailure(
        response.statusCode,
        (problem['detail'] ??
                problem['title'] ??
                'Server answered ${response.statusCode}.')
            .toString(),
        code: problem['code'] as String?,
      );
    } on AuthSessionRejected {
      return const ReportFailure(401, 'Your sign-in has expired.');
    } on Exception catch (error) {
      return ReportFailure(null, error.toString());
    }
  }
}
