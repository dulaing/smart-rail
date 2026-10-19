import 'dart:io';

import 'package:cronet_http/cronet_http.dart';
import 'package:http/http.dart' as http;

/// Use Android's certificate validation so debug builds trust the installed development CA
/// through network_security_config.xml, just as AppAuth does.
http.Client platformHttpClient() =>
    Platform.isAndroid ? CronetClient.defaultCronetEngine() : http.Client();
