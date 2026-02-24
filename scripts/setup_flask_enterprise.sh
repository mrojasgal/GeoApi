#!/usr/bin/env bash
set -euo pipefail

# Automated setup for the Flask + MySQL + Redis stack on Ubuntu.
# This script is intended for local/dev installation and can be run multiple times.

DB_NAME="${DB_NAME:-geoapi_db}"
DB_USER="${DB_USER:-geoapi_user}"
DB_PASSWORD="${DB_PASSWORD:-}"
APP_DIR="${APP_DIR:-$(pwd)}"
VENV_DIR="${VENV_DIR:-.venv}"
ENV_FILE="${ENV_FILE:-.env}"
SKIP_SYSTEM="${SKIP_SYSTEM:-0}"
SKIP_REDIS="${SKIP_REDIS:-0}"
NO_INTERACTIVE="${NO_INTERACTIVE:-0}"

print_help() {
  cat <<'EOF'
Usage:
  bash scripts/setup_flask_enterprise.sh [options]

Options:
  --db-name NAME            Database name (default: geoapi_db)
  --db-user USER            Database user (default: geoapi_user)
  --db-password PASS        Database user password (required if --no-interactive)
  --app-dir PATH            Project directory where venv/.env are created (default: current dir)
  --venv-dir NAME           Virtualenv directory name/path inside app-dir (default: .venv)
  --env-file NAME           Environment file name/path inside app-dir (default: .env)
  --skip-system             Skip apt install/upgrade steps
  --skip-redis              Skip redis installation and checks
  --no-interactive          Do not prompt for missing values
  -h, --help                Show this help

Examples:
  bash scripts/setup_flask_enterprise.sh
  bash scripts/setup_flask_enterprise.sh --db-password 'StrongPass_2026!' --no-interactive
  DB_NAME=geoapi_prod DB_USER=geoapi_app bash scripts/setup_flask_enterprise.sh
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --db-name)
      DB_NAME="$2"; shift 2;;
    --db-user)
      DB_USER="$2"; shift 2;;
    --db-password)
      DB_PASSWORD="$2"; shift 2;;
    --app-dir)
      APP_DIR="$2"; shift 2;;
    --venv-dir)
      VENV_DIR="$2"; shift 2;;
    --env-file)
      ENV_FILE="$2"; shift 2;;
    --skip-system)
      SKIP_SYSTEM=1; shift;;
    --skip-redis)
      SKIP_REDIS=1; shift;;
    --no-interactive)
      NO_INTERACTIVE=1; shift;;
    -h|--help)
      print_help; exit 0;;
    *)
      echo "Unknown argument: $1" >&2
      print_help
      exit 1;;
  esac
done

if ! command -v sudo >/dev/null 2>&1; then
  echo "Error: sudo is required for this script." >&2
  exit 1
fi

if [[ "${NO_INTERACTIVE}" -eq 1 && -z "${DB_PASSWORD}" ]]; then
  echo "Error: --db-password is required when using --no-interactive." >&2
  exit 1
fi

if [[ -z "${DB_PASSWORD}" ]]; then
  read -r -s -p "Enter MySQL password for user '${DB_USER}': " DB_PASSWORD
  echo
fi

if [[ -z "${DB_PASSWORD}" ]]; then
  echo "Error: DB password cannot be empty." >&2
  exit 1
fi

APP_DIR="$(realpath "${APP_DIR}")"
mkdir -p "${APP_DIR}"

VENV_PATH="${APP_DIR}/${VENV_DIR}"
ENV_PATH="${APP_DIR}/${ENV_FILE}"

echo "==> Configuration"
echo "APP_DIR=${APP_DIR}"
echo "DB_NAME=${DB_NAME}"
echo "DB_USER=${DB_USER}"
echo "VENV_PATH=${VENV_PATH}"
echo "ENV_PATH=${ENV_PATH}"

if [[ "${NO_INTERACTIVE}" -eq 0 ]]; then
  read -r -p "Proceed with installation? [y/N]: " CONFIRM
  CONFIRM="${CONFIRM:-N}"
  if [[ ! "${CONFIRM}" =~ ^[Yy]$ ]]; then
    echo "Installation aborted."
    exit 0
  fi
fi

if [[ "${SKIP_SYSTEM}" -eq 0 ]]; then
  echo "==> Installing system packages"
  sudo apt update
  sudo apt upgrade -y

  PKGS=(
    build-essential git curl wget unzip pkg-config
    python3 python3-venv python3-dev python3-pip
    mysql-server mysql-client libmysqlclient-dev
  )

  if [[ "${SKIP_REDIS}" -eq 0 ]]; then
    PKGS+=(redis-server)
  fi

  sudo apt install -y "${PKGS[@]}"
fi

echo "==> Enabling MySQL service"
sudo systemctl enable mysql >/dev/null 2>&1 || true
sudo systemctl start mysql

if [[ "${SKIP_REDIS}" -eq 0 ]]; then
  echo "==> Enabling Redis service"
  sudo systemctl enable redis-server >/dev/null 2>&1 || true
  sudo systemctl start redis-server
fi

echo "==> Creating database and app user (idempotent)"
ESCAPED_DB_PASS="${DB_PASSWORD//\'/\'\\\'\'}"
sudo mysql <<SQL
CREATE DATABASE IF NOT EXISTS \`${DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${ESCAPED_DB_PASS}';
ALTER USER '${DB_USER}'@'localhost' IDENTIFIED BY '${ESCAPED_DB_PASS}';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'localhost';
FLUSH PRIVILEGES;
SQL

echo "==> Creating virtual environment"
python3 -m venv "${VENV_PATH}"

# shellcheck disable=SC1090
source "${VENV_PATH}/bin/activate"
pip install --upgrade pip wheel

echo "==> Installing Python dependencies"
pip install \
  flask flask-login flask-sqlalchemy flask-migrate flask-wtf \
  python-dotenv email-validator bcrypt pymysql cryptography \
  marshmallow flask-marshmallow

if [[ "${SKIP_REDIS}" -eq 0 ]]; then
  pip install celery redis
fi

pip install openpyxl weasyprint pytest pytest-flask

if [[ ! -f "${ENV_PATH}" ]]; then
  echo "==> Creating ${ENV_PATH}"
  SECRET_KEY="$(openssl rand -hex 32)"
  cat > "${ENV_PATH}" <<EOF
FLASK_ENV=development
SECRET_KEY=${SECRET_KEY}
DATABASE_URL=mysql+pymysql://${DB_USER}:${DB_PASSWORD}@localhost:3306/${DB_NAME}
REDIS_URL=redis://localhost:6379/0
EOF
else
  echo "==> ${ENV_PATH} already exists, not overwriting."
fi

echo "==> Running validation checks"
python --version
pip --version
mysql -u "${DB_USER}" -p"${DB_PASSWORD}" -D "${DB_NAME}" -e "SELECT 'ok' AS test;"
python -c "import flask, flask_sqlalchemy, flask_migrate, flask_login; print('imports_ok')"

if [[ "${SKIP_REDIS}" -eq 0 ]]; then
  redis-cli ping
fi

echo
echo "Setup completed successfully."
echo "Activate your environment with:"
echo "  source ${VENV_PATH}/bin/activate"
