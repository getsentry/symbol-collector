#!/usr/bin/env bash

export batchId=$(uuidgen)
export batchFriendlyName="curl-test-batch"
export batchType="Android"
export body='{"BatchFriendlyName":"'${batchFriendlyName}'","BatchType":"'${batchType}'"}'
export server=http://localhost:5000

export startBatchUrl=${server}/symbol/batch/${batchId}/start
echo -e "\nStarting batch at: $startBatchUrl \n"

curl -sD - --header "Content-Type: application/json" --request POST \
  --data "$body" \
  ${startBatchUrl}


export uploadFiles=$server/symbol/batch/$batchId/upload
echo -e "\nUploading files: $uploadFiles \n"

curl -i \
  -F "libxamarin-app-arm64-v8a.so=@test/TestFiles/libxamarin-app-arm64-v8a.so" \
  -F "libxamarin-app.so=@test/TestFiles/libxamarin-app.so" \
  ${uploadFiles}

export closeBatchUrl=${server}/symbol/batch/${batchId}/close
echo -e "\nClosing batch at: $closeBatchUrl \n"

curl -sD - --header "Content-Type: application/json" --request POST \
  --data "{}" \
  ${closeBatchUrl}
