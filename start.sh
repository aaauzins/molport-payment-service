#!/bin/bash
# Starts the payment service. Run inside a screen session:
#   screen -S payment-service
#   ./start.sh
cd "$(dirname "$0")"
./PaymentService 2>&1 | tee -a payment-service.log
