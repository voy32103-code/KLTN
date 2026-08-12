"""
Unit tests for retry_handler.py

Tests cover:
1. Successful function execution (no retry needed)
2. Retryable errors with exponential backoff
3. Non-retryable errors (immediate failure)
4. Max retries exhaustion
5. JSON parsing strategies (4 strategies)
6. Backoff delay calculation
7. HTTP status code classification
"""
import pytest
import asyncio
import json
from unittest.mock import AsyncMock, patch
from typing import Any

from app.utils.retry_handler import (
    retry_async,
    with_retry,
    parse_json_with_retry,
    calculate_backoff_delay,
    is_retryable_http_status,
    RetryConfig,
    RetryableError,
    RateLimitError,
    TransientError,
    JSONParsingError,
    AI_SERVICE_RETRY_CONFIG
)


class TestRetryConfig:
    """Test RetryConfig initialization"""
    
    def test_default_config(self):
        config = RetryConfig()
        assert config.max_retries == 3
        assert config.initial_delay == 1.0
        assert config.max_delay == 60.0
        assert config.exponential_base == 2.0
        assert config.jitter is True
    
    def test_custom_config(self):
        config = RetryConfig(
            max_retries=5,
            initial_delay=0.5,
            max_delay=30.0,
            exponential_base=3.0,
            jitter=False
        )
        assert config.max_retries == 5
        assert config.initial_delay == 0.5
        assert config.max_delay == 30.0
        assert config.exponential_base == 3.0
        assert config.jitter is False


class TestBackoffCalculation:
    """Test exponential backoff delay calculation"""
    
    def test_exponential_growth(self):
        config = RetryConfig(
            initial_delay=1.0,
            exponential_base=2.0,
            max_delay=100.0,
            jitter=False
        )
        
        # Attempt 0: 1.0 * (2^0) = 1.0
        assert calculate_backoff_delay(0, config) == 1.0
        
        # Attempt 1: 1.0 * (2^1) = 2.0
        assert calculate_backoff_delay(1, config) == 2.0
        
        # Attempt 2: 1.0 * (2^2) = 4.0
        assert calculate_backoff_delay(2, config) == 4.0
        
        # Attempt 3: 1.0 * (2^3) = 8.0
        assert calculate_backoff_delay(3, config) == 8.0
    
    def test_max_delay_cap(self):
        config = RetryConfig(
            initial_delay=10.0,
            exponential_base=2.0,
            max_delay=20.0,
            jitter=False
        )
        
        # Should cap at max_delay
        assert calculate_backoff_delay(5, config) == 20.0
        assert calculate_backoff_delay(10, config) == 20.0
    
    def test_jitter_adds_randomness(self):
        config = RetryConfig(
            initial_delay=10.0,
            exponential_base=2.0,
            max_delay=100.0,
            jitter=True
        )
        
        # Run multiple times, should get different values
        delays = [calculate_backoff_delay(1, config) for _ in range(10)]
        
        # Should have some variation (not all identical)
        assert len(set(delays)) > 1
        
        # Should be within ±25% of base delay (20.0)
        base_delay = 20.0
        for delay in delays:
            assert 15.0 <= delay <= 25.0


class TestRetryableErrors:
    """Test error classification"""
    
    def test_retryable_error_inheritance(self):
        assert issubclass(RateLimitError, RetryableError)
        assert issubclass(TransientError, RetryableError)
    
    def test_error_instantiation(self):
        err1 = RateLimitError("Rate limit exceeded")
        assert str(err1) == "Rate limit exceeded"
        
        err2 = TransientError("Network timeout")
        assert str(err2) == "Network timeout"


class TestHTTPStatusClassification:
    """Test HTTP status code retryability"""
    
    def test_retryable_status_codes(self):
        # Should be retryable
        assert is_retryable_http_status(408) is True  # Request Timeout
        assert is_retryable_http_status(429) is True  # Too Many Requests
        assert is_retryable_http_status(500) is True  # Internal Server Error
        assert is_retryable_http_status(502) is True  # Bad Gateway
        assert is_retryable_http_status(503) is True  # Service Unavailable
        assert is_retryable_http_status(504) is True  # Gateway Timeout
    
    def test_non_retryable_status_codes(self):
        # Should NOT be retryable
        assert is_retryable_http_status(200) is False  # OK
        assert is_retryable_http_status(400) is False  # Bad Request
        assert is_retryable_http_status(401) is False  # Unauthorized
        assert is_retryable_http_status(403) is False  # Forbidden
        assert is_retryable_http_status(404) is False  # Not Found


@pytest.mark.asyncio
class TestRetryAsync:
    """Test async retry logic"""
    
    async def test_success_no_retry(self):
        """Function succeeds on first try - no retry needed"""
        mock_func = AsyncMock(return_value="success")
        
        result = await retry_async(
            mock_func,
            config=RetryConfig(max_retries=3)
        )
        
        assert result == "success"
        assert mock_func.call_count == 1
    
    async def test_retryable_error_then_success(self):
        """Function fails twice with retryable error, then succeeds"""
        mock_func = AsyncMock(
            side_effect=[
                RateLimitError("Rate limit 1"),
                TransientError("Timeout 2"),
                "success"
            ]
        )
        
        config = RetryConfig(max_retries=3, initial_delay=0.01, jitter=False)
        result = await retry_async(mock_func, config=config)
        
        assert result == "success"
        assert mock_func.call_count == 3
    
    async def test_max_retries_exhausted(self):
        """All retries exhausted - should raise last exception"""
        mock_func = AsyncMock(
            side_effect=RateLimitError("Always fails")
        )
        
        config = RetryConfig(max_retries=2, initial_delay=0.01, jitter=False)
        
        with pytest.raises(RateLimitError, match="Always fails"):
            await retry_async(mock_func, config=config)
        
        # Should try: initial + 2 retries = 3 total
        assert mock_func.call_count == 3
    
    async def test_non_retryable_error_immediate_fail(self):
        """Non-retryable error should fail immediately without retry"""
        mock_func = AsyncMock(
            side_effect=ValueError("Invalid input")
        )
        
        config = RetryConfig(max_retries=3, initial_delay=0.01)
        
        with pytest.raises(ValueError, match="Invalid input"):
            await retry_async(mock_func, config=config)
        
        # Should only try once (no retry)
        assert mock_func.call_count == 1
    
    async def test_retry_with_args_kwargs(self):
        """Retry should pass through args and kwargs"""
        mock_func = AsyncMock(return_value="result")
        
        await retry_async(
            mock_func,
            "arg1", "arg2",
            config=RetryConfig(max_retries=1),
            kwarg1="value1",
            kwarg2="value2"
        )
        
        mock_func.assert_called_once_with("arg1", "arg2", kwarg1="value1", kwarg2="value2")


@pytest.mark.asyncio
class TestWithRetryDecorator:
    """Test @with_retry decorator"""
    
    async def test_decorator_success(self):
        """Decorated function should work normally on success"""
        
        @with_retry(config=RetryConfig(max_retries=2))
        async def my_func(x: int) -> int:
            return x * 2
        
        result = await my_func(5)
        assert result == 10
    
    async def test_decorator_with_retry(self):
        """Decorated function should retry on retryable errors"""
        call_count = 0
        
        @with_retry(config=RetryConfig(max_retries=2, initial_delay=0.01, jitter=False))
        async def flaky_func():
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                raise TransientError(f"Attempt {call_count}")
            return "success"
        
        result = await flaky_func()
        assert result == "success"
        assert call_count == 3


@pytest.mark.asyncio
class TestJSONParsing:
    """Test JSON parsing with multiple strategies"""
    
    async def test_strategy_1_direct_parse(self):
        """Strategy 1: Direct json.loads() should work for valid JSON"""
        valid_json = '{"key": "value", "number": 42}'
        result = await parse_json_with_retry(valid_json)
        
        assert result == {"key": "value", "number": 42}
    
    async def test_strategy_2_markdown_code_block(self):
        """Strategy 2: Extract JSON from markdown code blocks"""
        markdown_json = '''
Here is the JSON:

```json
{
  "actor": "Customer",
  "action": "Book",
  "object": "Room"
}
```
'''
        result = await parse_json_with_retry(markdown_json)
        
        assert result == {
            "actor": "Customer",
            "action": "Book",
            "object": "Room"
        }
    
    async def test_strategy_2_markdown_without_language(self):
        """Strategy 2: Markdown block without 'json' language tag"""
        markdown_json = '''```
{"status": "ok"}
```'''
        result = await parse_json_with_retry(markdown_json)
        
        assert result == {"status": "ok"}
    
    async def test_strategy_3_strip_prefixes(self):
        """Strategy 3: Remove common prefixes like 'Here is the JSON:'"""
        prefixed_json = 'Here is the JSON: {"result": true}'
        result = await parse_json_with_retry(prefixed_json)
        
        assert result == {"result": True}
    
    async def test_strategy_4_fix_trailing_commas(self):
        """Strategy 4: Fix trailing commas (common AI mistake)"""
        json_with_trailing_comma = '{"items": [1, 2, 3,], "end": "value",}'
        result = await parse_json_with_retry(json_with_trailing_comma)
        
        assert result == {"items": [1, 2, 3], "end": "value"}
    
    async def test_strategy_4_fix_single_quotes(self):
        """Strategy 4: Convert single quotes to double quotes"""
        json_single_quotes = "{'name': 'John', 'age': 30}"
        result = await parse_json_with_retry(json_single_quotes)
        
        assert result == {"name": "John", "age": 30}
    
    async def test_all_strategies_fail(self):
        """Should raise JSONParsingError if all strategies fail"""
        invalid_json = "This is not JSON at all { broken }"
        
        with pytest.raises(JSONParsingError):
            await parse_json_with_retry(invalid_json)
    
    async def test_complex_nested_json(self):
        """Should handle complex nested structures"""
        complex_json = '''
```json
{
  "requirements": [
    {
      "id": "REQ001",
      "actor": "User",
      "action": "Login",
      "object": "System",
      "condition": "Valid credentials",
      "type": "FR"
    }
  ],
  "metadata": {
    "version": "1.0",
    "count": 1
  }
}
```
'''
        result = await parse_json_with_retry(complex_json)
        
        assert isinstance(result, dict)
        assert len(result["requirements"]) == 1
        assert result["requirements"][0]["id"] == "REQ001"
        assert result["metadata"]["version"] == "1.0"
    
    async def test_json_array(self):
        """Should handle JSON arrays"""
        json_array = '[{"a": 1}, {"b": 2}, {"c": 3}]'
        result = await parse_json_with_retry(json_array)
        
        assert isinstance(result, list)
        assert len(result) == 3
        assert result[0] == {"a": 1}


class TestAIServiceRetryConfig:
    """Test the default AI service retry configuration"""
    
    def test_ai_service_defaults(self):
        """Verify AI_SERVICE_RETRY_CONFIG has sensible defaults"""
        assert AI_SERVICE_RETRY_CONFIG.max_retries == 3
        assert AI_SERVICE_RETRY_CONFIG.initial_delay == 2.0
        assert AI_SERVICE_RETRY_CONFIG.max_delay == 30.0
        assert AI_SERVICE_RETRY_CONFIG.exponential_base == 2.0
        assert AI_SERVICE_RETRY_CONFIG.jitter is True


@pytest.mark.asyncio
class TestEdgeCases:
    """Test edge cases and boundary conditions"""
    
    async def test_zero_retries(self):
        """Config with max_retries=0 should only try once"""
        mock_func = AsyncMock(side_effect=TransientError("Fail"))
        
        config = RetryConfig(max_retries=0, initial_delay=0.01)
        
        with pytest.raises(TransientError):
            await retry_async(mock_func, config=config)
        
        assert mock_func.call_count == 1
    
    async def test_empty_json_string(self):
        """Empty JSON objects/arrays should parse correctly"""
        assert await parse_json_with_retry("{}") == {}
        assert await parse_json_with_retry("[]") == []
    
    async def test_json_with_unicode(self):
        """Should handle Unicode characters"""
        unicode_json = '{"message": "Xin chào", "emoji": "🎉"}'
        result = await parse_json_with_retry(unicode_json)
        
        assert isinstance(result, dict)
        assert result["message"] == "Xin chào"
        assert result["emoji"] == "🎉"
    
    async def test_very_long_json(self):
        """Should handle large JSON payloads"""
        large_array = [{"id": i, "value": f"item_{i}"} for i in range(1000)]
        json_str = json.dumps(large_array)
        
        result = await parse_json_with_retry(json_str)
        
        assert len(result) == 1000
        assert result[0]["id"] == 0
        assert result[999]["value"] == "item_999"


# Run tests with: pytest tests/test_retry_handler.py -v
# Run with coverage: pytest tests/test_retry_handler.py --cov=app.utils.retry_handler --cov-report=term-missing
