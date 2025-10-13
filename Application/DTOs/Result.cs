using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Application.DTOs
{
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public List<string> Errors { get; }

        private Result(T value)
        {
            IsSuccess = true;
            Value = value;
            Errors = new List<string>();
        }

        private Result(List<string> errors)
        {
            IsSuccess = false;
            Errors = errors;
        }

        public static Result<T> Success(T value) => new Result<T>(value);

        public static Result<T> Failure(List<string> errors) => new Result<T>(errors);
    }

}
