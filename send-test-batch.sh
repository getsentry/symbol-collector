export batchId=$(uuidgen)
export batchFriendlyName="curl-test-batch"
export batchType="Android"
export body='{"BatchFriendlyName":"'$batchFriendlyName'","BatchType":"'$batchType'"}'
export server=http://localhost:5000

curl -sD - --header "Content-Type: application/json" --request POST \
  --data "$body" \
  $server/symbol/batch/$batchId/start

curl -i \
  -F "libxamarin-app-arm64-v8a.so=@test/TestFiles/libxamarin-app-arm64-v8a.so" \
  -F "libxamarin-app.so=@test/TestFiles/libxamarin-app.so" \
  $server/symbol/batch/$batchId/upload

curl -sD - --header "Content-Type: application/json" --request POST \
  --data "{}" \
  $server/symbol/batch/$batchId/close
