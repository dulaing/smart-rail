import 'dart:io';

import 'package:cronet_http/cronet_http.dart';
import 'package:http/http.dart' as http;

/// The HTTP client the driver app uses for SmartRail API calls.
///
/// On Android, Dart's own client trusts only the built-in system certificate authorities, so a
/// local development certificate fails its handshake. Cronet verifies certificates through
/// Android and therefore follows `network_security_config.xml`, letting debug builds trust the
/// development CA that Keycloak and the API present.
http.Client platformHttpClient() =>
    Platform.isAndroid ? CronetClient.defaultCronetEngine() : http.Client();
