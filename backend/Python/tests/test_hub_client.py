"""Tests for HubServiceClient (socket mocked)."""

from __future__ import annotations

import json
from unittest.mock import MagicMock, patch

import pytest

from hub_client import HubServiceClient


def test_hub_client_registers_and_sends_event() -> None:
    fake_sock = MagicMock()
    fake_sock.recv.return_value = b""

    with patch("hub_client.socket.socket", return_value=fake_sock):
        c = HubServiceClient("testsvc", host="127.0.0.1", port=5000, on_command=None)
        c.connect()
    hello = fake_sock.sendall.call_args_list[0][0][0].decode("utf-8").strip()
    assert json.loads(hello)["role"] == "service"
    assert json.loads(hello)["service"] == "testsvc"

    c.send_event({"fanout": "python_peers", "event": "plane_pose", "active": False})
    sent = fake_sock.sendall.call_args_list[-1][0][0].decode("utf-8").strip()
    msg = json.loads(sent)
    assert msg["event"] == "plane_pose"
    c.close()
