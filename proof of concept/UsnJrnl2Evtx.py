import os
import sys
import signal
import subprocess
import argparse
import json
import ctypes
import logging
from logging.handlers import RotatingFileHandler
import win32evtlogutil
import win32evtlog

# Configuration for custom event log
EVENT_SOURCE = "USNJournalLogger"
EVENT_LOG = "USNJournalLog"
EVENT_CATEGORY = 0
EVENT_ID = 1000
EVENT_TYPE = win32evtlog.EVENTLOG_INFORMATION_TYPE
USN_STATE_FILE = 'last_usn.txt'

# Parsing keys for fsutil output
header_keys = [
    b'USN Journal ID', b'First USN', b'Next USN', b'Start USN',
    b'Min major version', b'Max major version'
]
entry_keys = [
    b'Usn', b'File name', b'File name length', b'Reason',
    b'Time stamp', b'File attributes', b'File ID', b'Parent file ID',
    b'Source info', b'Security ID', b'Major version', b'Minor version',
    b'Record length'
]

# Initialize module-level logger
logger = logging.getLogger('USNJournalLogger')

# Default logging config constants
DEFAULT_LOG_FILE = 'usn_journal_logger.log'
DEFAULT_LOG_LEVEL = 'DEBUG'
DEFAULT_MAX_BYTES = 10 * 1024 * 1024
DEFAULT_BACKUP_COUNT = 1
DEFAULT_LOG_FORMAT = '%(asctime)s %(levelname)s %(message)s'


def configure_logging(args):
    # Configure file and optional console logging based on args
    level = getattr(logging, args.log_level.upper(), logging.INFO)
    logger.setLevel(level)
    # Remove existing handlers
    for h in list(logger.handlers):
        logger.removeHandler(h)
    # File handler
    fh = RotatingFileHandler(
        args.log_file,
        maxBytes=args.log_max_bytes,
        backupCount=args.log_backup_count
    )
    fmt = logging.Formatter(DEFAULT_LOG_FORMAT)
    fh.setFormatter(fmt)
    logger.addHandler(fh)
    # Console handler if requested
    if args.console:
        ch = logging.StreamHandler()
        ch.setFormatter(fmt)
        logger.addHandler(ch)


def is_admin():
    # Return True if running with administrative privileges
    try:
        return ctypes.windll.shell32.IsUserAnAdmin()  # type: ignore
    except Exception:
        return False


def elevate():
    # Relaunch the script with admin privileges via UAC
    script = os.path.abspath(sys.argv[0])
    params = ' '.join(f'"{arg}"' for arg in sys.argv[1:])
    ctypes.windll.shell32.ShellExecuteW(None, "runas", sys.executable,
                                        f'"{script}" {params}', None, 1)
    sys.exit(0)


def signal_handler(signum, frame):
    # Handle termination signals
    logger.info("Signal %s received, exiting", signum)
    sys.exit(0)


def load_last_usn():
    # Read the last processed USN from disk
    logger.debug("Loading last USN from %s", USN_STATE_FILE)
    if os.path.exists(USN_STATE_FILE):
        with open(USN_STATE_FILE, 'r') as f:
            usn = f.read().strip()
        logger.debug("Found last_usn: %s", usn)
        return usn
    logger.debug("No state file, defaulting to '0'")
    return '0'


def save_next_usn(usn):
    # Persist the next USN for later runs
    logger.debug("Saving next_usn %s to %s", usn, USN_STATE_FILE)
    with open(USN_STATE_FILE, 'w') as f:
        f.write(usn)


def entry_to_dict(lines, keys):
    # Convert raw byte lines and keys into a dictionary
    logger.debug("Parsing %d lines into dict", len(lines))
    data = {}
    for key, line in zip(keys, lines):
        parts = line.split(b":", 1)
        if len(parts) == 2:
            val = parts[1].strip().decode(errors='ignore')
            data[key.decode()] = val
            logger.debug("Parsed %s: %s", key.decode(), val)
    return data


def enhance_entry(entry, args):
    if args.complete_filenames:
        cmd = ['fsutil', 'file', 'queryFileNameById', args.volume, f"0x{entry['Parent file ID']}"]
        logger.debug("Executing fsutil with cmd: %s", cmd)
        proc = subprocess.Popen(cmd, stdout=subprocess.PIPE)
        out, _ = proc.communicate()
        line = out.strip()
        if b' is ' in line:
            parent_file_path = line.split(b' is ', 1)[1].decode(errors='ignore')
            entry['File name'] = f"{parent_file_path}\\{entry['File name']}"
        proc.wait()
    return entry


def report_event(entry, args):
    # Serialize entry as JSON and write to Windows Event Log
    logger.debug("Reporting entry: %s", entry)
    entry = enhance_entry(entry, args)
    try:
        msg = json.dumps(entry, ensure_ascii=False)
    except Exception as e:
        logger.error("JSON serialization error: %s", e)
        msg = str(entry)
    try:
        win32evtlogutil.ReportEvent(
            EVENT_SOURCE,
            EVENT_ID,
            EVENT_CATEGORY,
            EVENT_TYPE,
            [msg]
        )
        logger.debug("Windows event logged successfully")
    except Exception as e:
        logger.error("Failed to log event: %s", e)


def parse_journal(cmd, args):
    # Run fsutil, parse header and entries synchronously
    logger.debug("Executing fsutil with cmd: %s", cmd)
    header_lines, entry_lines = [], []
    header_parsed = False
    header = {}
    proc = subprocess.Popen(cmd, stdout=subprocess.PIPE)
    for raw in proc.stdout:
        line = raw.strip()
        logger.debug("fsutil output: %s", line)
        if line == b'':
            if not header_parsed:
                logger.debug("Parsing header block")
                header = entry_to_dict(header_lines, header_keys)
                nxt = header.get('Next USN')
                if nxt:
                    save_next_usn(nxt)
                header_parsed = True
                header_lines.clear()
                logger.debug("Header parsed: %s", header)
            else:
                logger.debug("Parsing entry block of %d lines", len(entry_lines))
                entry = entry_to_dict(entry_lines, entry_keys)
                entry.update(header)
                report_event(entry, args)
                entry_lines.clear()
        else:
            (header_lines if not header_parsed else entry_lines).append(raw)
    proc.wait()
    logger.debug("fsutil process exited with code %d", proc.returncode)


def main():
    # Elevate if needed
    if not is_admin():
        logger.info("Not running as admin, requesting elevation")
        elevate()

    # Argument parsing
    # TODO: Review default args before release
    parser = argparse.ArgumentParser()
    parser.add_argument('--volume', '-vol', default='C:',
                        help='Volume drive letter')
    parser.add_argument('--complete-filenames', action='store_true',
                        help='Query absolute filepath. Incurs some time overhead')
    parser.add_argument('--log-file', default=DEFAULT_LOG_FILE,
                        help='Path to rolling log file')
    parser.add_argument('--log-level', default=DEFAULT_LOG_LEVEL,
                        choices=['DEBUG', 'INFO', 'WARNING', 'ERROR', 'CRITICAL'],
                        help='Logging level')
    parser.add_argument('--log-max-bytes', type=int,
                        default=DEFAULT_MAX_BYTES,
                        help='Max bytes per log file before rotation')
    parser.add_argument('--log-backup-count', type=int,
                        default=DEFAULT_BACKUP_COUNT,
                        help='Number of backup log files to keep')
    parser.add_argument('--console', action='store_true',
                        help='Also print logs to console')
    args = parser.parse_args()

    # Configure logging
    configure_logging(args)
    logger.info("Starting USN Journal Logger script")

    # Register event source
    try:
        win32evtlogutil.AddSourceToRegistry(
            EVENT_SOURCE,
            sys.executable,
            EVENT_LOG,
            msgDLL=None,  # No custom messages
            eventCategory=0,
            eventID=1000,
            typesSupported=win32evtlog.EVENTLOG_INFORMATION_TYPE
        )
        logger.debug("Event source registered in registry")
    except Exception as e:
        logger.warning("Registry registration may require elevation: %s", e)

    # Signal handlers
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)

    # Load last USN and run
    last_usn = load_last_usn()
    logger.info("Starting journal read at USN %s", last_usn)
    cmd = ['fsutil', 'usn', 'readJournal', args.volume,
           f'startusn={last_usn}']
    parse_journal(cmd, args)
    logger.info("USN Journal Logger script complete")


if __name__ == '__main__':
    main()
