#!/bin/bash

# Decode base64 secrets into cert files
echo "$TLS_CERT_B64" | base64 -d > /etc/nginx/certs/cert.pem
echo "$TLS_KEY_B64"  | base64 -d > /etc/nginx/certs/key.pem

# Fix permissions for NGINX
chmod 600 /etc/nginx/certs/*.pem

# Start NGINX
nginx -g "daemon off;"