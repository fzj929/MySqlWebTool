#!/usr/bin/env bash
set -euo pipefail

ACTION=${1:-}
SERVICE_NAME=${2:-DataPilot}
PORT=${3:-5080}
SERVICE_USER=${4:-${SUDO_USER:-root}}
DOTNET_OVERRIDE=${5:-${DOTNET_PATH:-}}

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
APP_DLL="${SCRIPT_DIR}/DataPilot.Api.dll"

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

    DOTNET_PATH="$DOTNET_OVERRIDE"
    if [[ -z "$DOTNET_PATH" ]]; then
      DOTNET_PATH=$(command -v dotnet || true)
    fi
    if [[ -z "$DOTNET_PATH" ]]; then
      # sudo may replace PATH. Probe standard and invoking-user install locations.
      candidates=()
      if [[ -n "${DOTNET_ROOT:-}" ]]; then
        candidates+=("$DOTNET_ROOT/dotnet")
      fi
      candidates+=(/usr/bin/dotnet /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet
        /usr/local/share/dotnet/dotnet /opt/dotnet/dotnet)
      for account in "${SUDO_USER:-$SERVICE_USER}" "$SERVICE_USER"; do
        account_home=$(getent passwd "$account" | cut -d: -f6 || true)
        if [[ -n "$account_home" ]]; then
          candidates+=("$account_home/.dotnet/dotnet")
        fi
      done
      for candidate in "${candidates[@]}"; do
        if [[ -f "$candidate" && -x "$candidate" ]]; then
          DOTNET_PATH="$candidate"
          break
        fi
      done
    fi
    if [[ "$DOTNET_PATH" != /* || ! -f "$DOTNET_PATH" || ! -x "$DOTNET_PATH" ]]; then
      echo "Cannot locate an executable dotnet. sudo may use a different PATH." >&2
      echo 'Run from your normal-user shell: sudo env DOTNET_PATH="$(command -v dotnet)" ./install-service.sh' >&2
      echo "Or pass an absolute dotnet path as the fourth install-service.sh argument." >&2
      exit 1
    fi
    # Verify runtime availability and permissions under the actual service identity.
    if ! RUNTIMES=$(runuser -u "$SERVICE_USER" -- "$DOTNET_PATH" --list-runtimes); then
      echo "User '$SERVICE_USER' cannot run '$DOTNET_PATH'. Check directory permissions and the installation." >&2
      exit 1
    fi
    if ! grep -Eq '^Microsoft.AspNetCore.App 8\.' <<< "$RUNTIMES" ||
       ! grep -Eq '^Microsoft.NETCore.App 8\.' <<< "$RUNTIMES"; then
      echo "ASP.NET Core 8 Runtime is required. Runtimes visible to '$SERVICE_USER':" >&2
      printf '%s\n' "$RUNTIMES" >&2
      exit 1
    fi
    echo "Using dotnet: $DOTNET_PATH"

    DATA_DIR="$(dirname "$SCRIPT_DIR")/DataPilot-data"
    install -d -m 0700 -o "$SERVICE_USER" "$DATA_DIR"

    # WorkingDirectory is a single path, not an ExecStart-style quoted word.
    # Only systemd specifiers need escaping here; surrounding quotes become
    # literal path characters on supported systemd versions.
    ESCAPED_DIR=${SCRIPT_DIR//%/%%}
    ESCAPED_DLL=$(escape_systemd_value "$APP_DLL")
    ESCAPED_DOTNET=$(escape_systemd_value "$DOTNET_PATH")
    TEMP_UNIT_DIR=$(mktemp -d)
    TEMP_UNIT="$TEMP_UNIT_DIR/$SERVICE_NAME.service"
    trap 'rm -f -- "$TEMP_UNIT"; rmdir -- "$TEMP_UNIT_DIR"' EXIT

    {
      echo "[Unit]"
      echo "Description=DataPilot"
      echo "After=network-online.target"
      echo "Wants=network-online.target"
      echo
      echo "[Service]"
      echo "Type=simple"
      echo "User=$SERVICE_USER"
      printf 'WorkingDirectory=%s\n' "$ESCAPED_DIR"
      echo "ExecStart=\"$ESCAPED_DOTNET\" \"$ESCAPED_DLL\" --urls \"http://0.0.0.0:$PORT\""
      echo "Environment=ASPNETCORE_ENVIRONMENT=Production"
      echo "Environment=\"DataPilot__SqliteDirectory=$(escape_systemd_value "$DATA_DIR")\""
      echo "Restart=on-failure"
      echo "RestartSec=5"
      echo "KillSignal=SIGINT"
      echo "SyslogIdentifier=$SERVICE_NAME"
      echo
      echo "[Install]"
      echo "WantedBy=multi-user.target"
    } > "$TEMP_UNIT"

    if command -v systemd-analyze >/dev/null 2>&1; then
      if ! systemd-analyze verify "$TEMP_UNIT"; then
        echo "Generated unit failed validation; the existing service file has not been replaced." >&2
        cat "$TEMP_UNIT" >&2
        exit 1
      fi
    fi
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
    echo "Usage: $0 {install|start|stop|restart|uninstall} [service-name] [port] [linux-user] [dotnet-path]" >&2
    exit 2
    ;;
esac
