#!/usr/bin/env bash
set -euo pipefail

ACTION=${1:-}
SERVICE_NAME=${2:-MySqlWebTool}
PORT=${3:-5080}
SERVICE_USER=${4:-${SUDO_USER:-root}}

if [[ ! "$SERVICE_NAME" =~ ^[A-Za-z0-9_.-]+$ ]]; then
  echo "Invalid service name: $SERVICE_NAME" >&2
  exit 2
fi

if [[ ! "$PORT" =~ ^[0-9]+$ ]] || (( PORT < 1 || PORT > 65535 )); then
  echo "Port must be between 1 and 65535." >&2
  exit 2
fi

if (( EUID != 0 )); then
  echo "Run this script with sudo or as root." >&2
  exit 1
fi

SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
SCRIPT_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
APP_DLL="${SCRIPT_DIR}/MySqlTool.Api.dll"

escape_systemd_value() {
  printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' -e 's/%/%%/g'
}

case "$ACTION" in
  install)
    if [[ ! -f "$APP_DLL" ]]; then
      echo "Application file was not found: $APP_DLL" >&2
      exit 1
    fi
    if ! id "$SERVICE_USER" >/dev/null 2>&1; then
      echo "Linux user does not exist: $SERVICE_USER" >&2
      exit 1
    fi

    DOTNET_PATH=$(command -v dotnet || true)
    if [[ -z "$DOTNET_PATH" ]]; then
      echo "dotnet was not found. Install ASP.NET Core 8 Runtime first." >&2
      exit 1
    fi

    ESCAPED_DIR=$(escape_systemd_value "$SCRIPT_DIR")
    ESCAPED_DLL=$(escape_systemd_value "$APP_DLL")
    ESCAPED_DOTNET=$(escape_systemd_value "$DOTNET_PATH")
    TEMP_UNIT=$(mktemp)
    trap 'rm -f "$TEMP_UNIT"' EXIT

    {
      echo "[Unit]"
      echo "Description=MySQL Web Tool"
      echo "After=network-online.target"
      echo "Wants=network-online.target"
      echo
      echo "[Service]"
      echo "Type=simple"
      echo "User=$SERVICE_USER"
      echo "WorkingDirectory=\"$ESCAPED_DIR\""
      echo "ExecStart=\"$ESCAPED_DOTNET\" \"$ESCAPED_DLL\" --urls \"http://0.0.0.0:$PORT\""
      echo "Environment=ASPNETCORE_ENVIRONMENT=Production"
      echo "Restart=on-failure"
      echo "RestartSec=5"
      echo "KillSignal=SIGINT"
      echo "SyslogIdentifier=$SERVICE_NAME"
      echo
      echo "[Install]"
      echo "WantedBy=multi-user.target"
    } > "$TEMP_UNIT"

    install -m 0644 "$TEMP_UNIT" "$SERVICE_FILE"
    systemctl daemon-reload
    systemctl enable --now "$SERVICE_NAME"
    echo "Service '$SERVICE_NAME' installed and started on port $PORT as user '$SERVICE_USER'."
    ;;
  start)
    systemctl start "$SERVICE_NAME"
    echo "Service '$SERVICE_NAME' is running."
    ;;
  stop)
    systemctl stop "$SERVICE_NAME"
    echo "Service '$SERVICE_NAME' is stopped."
    ;;
  restart)
    systemctl restart "$SERVICE_NAME"
    echo "Service '$SERVICE_NAME' restarted."
    ;;
  uninstall)
    systemctl disable --now "$SERVICE_NAME" 2>/dev/null || true
    rm -f -- "$SERVICE_FILE"
    systemctl daemon-reload
    systemctl reset-failed "$SERVICE_NAME" 2>/dev/null || true
    echo "Service '$SERVICE_NAME' uninstalled."
    ;;
  *)
    echo "Usage: $0 {install|start|stop|restart|uninstall} [service-name] [port] [linux-user]" >&2
    exit 2
    ;;
esac
